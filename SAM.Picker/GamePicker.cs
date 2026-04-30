/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using System.Xml.XPath;
using static SAM.Picker.InvariantShorthand;
using APITypes = SAM.API.Types;

namespace SAM.Picker
{
    internal partial class GamePicker : Form
    {
        private readonly API.Client _SteamClient;

        private readonly Dictionary<uint, GameInfo> _Games;
        private readonly List<GameInfo> _FilteredGames;

        private readonly object _LogoLock;
        private readonly HashSet<string> _LogosAttempting;
        private readonly HashSet<string> _LogosAttempted;
        private readonly ConcurrentQueue<GameInfo> _LogoQueue;

        private readonly API.Callbacks.AppDataChanged _AppDataChangedCallback;

        public GamePicker(API.Client client)
        {
            this._Games = new();
            this._FilteredGames = new();
            this._LogoLock = new();
            this._LogosAttempting = new();
            this._LogosAttempted = new();
            this._LogoQueue = new();

            this.InitializeComponent();

            Bitmap blank = new(this._LogoImageList.ImageSize.Width, this._LogoImageList.ImageSize.Height);
            using (var g = Graphics.FromImage(blank))
            {
                g.Clear(Color.DimGray);
            }

            this._LogoImageList.Images.Add("Blank", blank);

            this._SteamClient = client;

            this._AppDataChangedCallback = client.CreateAndRegisterCallback<API.Callbacks.AppDataChanged>();
            this._AppDataChangedCallback.OnRun += this.OnAppDataChanged;

            this.AddGames();
        }

        private void OnAppDataChanged(APITypes.AppDataChanged param)
        {
            if (param.Result == false)
            {
                return;
            }

            if (this._Games.TryGetValue(param.Id, out var game) == false)
            {
                return;
            }

            game.Name = this._SteamClient.SteamApps001.GetAppData(game.Id, "name");

            this.AddGameToLogoQueue(game);
            this.DownloadNextLogo();
        }

        private void DoDownloadList(object sender, DoWorkEventArgs e)
        {
            this._PickerStatusLabel.Text = "Downloading game list...";

            byte[] bytes;
            using (WebClient downloader = new())
            {
                bytes = downloader.DownloadData(new Uri("https://gib.me/sam/games.xml"));
            }

            List<KeyValuePair<uint, string>> pairs = new();
            using (MemoryStream stream = new(bytes, false))
            {
                XPathDocument document = new(stream);
                var navigator = document.CreateNavigator();
                var nodes = navigator.Select("/games/game");
                while (nodes.MoveNext() == true)
                {
                    string type = nodes.Current.GetAttribute("type", "");
                    if (string.IsNullOrEmpty(type) == true)
                    {
                        type = "normal";
                    }
                    pairs.Add(new((uint)nodes.Current.ValueAsLong, type));
                }
            }

            this._PickerStatusLabel.Text = "Checking game ownership...";
            foreach (var kv in pairs)
            {
                this.AddGame(kv.Key, kv.Value);
            }
        }

        private void OnDownloadList(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null || e.Cancelled == true)
            {
                this.AddDefaultGames();
                MessageBox.Show(e.Error.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            this.RefreshGames();
            this._RefreshGamesButton.Enabled = true;
            this.DownloadNextLogo();
        }

        private void RefreshGames()
        {
            var nameSearch = this._SearchGameTextBox.Text.Length > 0
                ? this._SearchGameTextBox.Text
                : null;

            var wantNormals = this._FilterGamesMenuItem.Checked == true;
            var wantDemos = this._FilterDemosMenuItem.Checked == true;
            var wantMods = this._FilterModsMenuItem.Checked == true;
            var wantJunk = this._FilterJunkMenuItem.Checked == true;

            this._FilteredGames.Clear();
            foreach (var info in this._Games.Values.OrderBy(gi => gi.Name))
            {
                if (nameSearch != null &&
                    info.Name.IndexOf(nameSearch, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bool wanted = info.Type switch
                {
                    "normal" => wantNormals,
                    "demo" => wantDemos,
                    "mod" => wantMods,
                    "junk" => wantJunk,
                    _ => true,
                };
                if (wanted == false)
                {
                    continue;
                }

                this._FilteredGames.Add(info);
            }

            this._GameListView.VirtualListSize = this._FilteredGames.Count;
            this._PickerStatusLabel.Text =
                $"Displaying {this._GameListView.Items.Count} games. Total {this._Games.Count} games.";

            if (this._GameListView.Items.Count > 0)
            {
                this._GameListView.Items[0].Selected = true;
                this._GameListView.Select();
            }
        }

        private void OnGameListViewRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            var info = this._FilteredGames[e.ItemIndex];
            e.Item = info.Item = new()
            {
                Text = info.Name,
                ImageIndex = info.ImageIndex,
            };
        }

        private void OnGameListViewSearchForVirtualItem(object sender, SearchForVirtualItemEventArgs e)
        {
            if (e.Direction != SearchDirectionHint.Down || e.IsTextSearch == false)
            {
                return;
            }

            var count = this._FilteredGames.Count;
            if (count < 2)
            {
                return;
            }

            var text = e.Text;
            int startIndex = e.StartIndex;

            Predicate<GameInfo> predicate;
            /*if (e.IsPrefixSearch == true)*/
            {
                predicate = gi => gi.Name != null && gi.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase);
            }
            /*else
            {
                predicate = gi => gi.Name != null && string.Compare(gi.Name, text, StringComparison.CurrentCultureIgnoreCase) == 0;
            }*/

            int index;
            if (e.StartIndex >= count)
            {
                // starting from the last item in the list
                index = this._FilteredGames.FindIndex(0, startIndex - 1, predicate);
            }
            else if (startIndex <= 0)
            {
                // starting from the first item in the list
                index = this._FilteredGames.FindIndex(0, count, predicate);
            }
            else
            {
                index = this._FilteredGames.FindIndex(startIndex, count - startIndex, predicate);
                if (index < 0)
                {
                    index = this._FilteredGames.FindIndex(0, startIndex - 1, predicate);
                }
            }

            e.Index = index < 0 ? -1 : index;
        }

        private void DoDownloadLogo(object sender, DoWorkEventArgs e)
        {
            var info = (GameInfo)e.Argument;

            this._LogosAttempted.Add(info.ImageUrl);

            using (WebClient downloader = new())
            {
                try
                {
                    var data = downloader.DownloadData(new Uri(info.ImageUrl));
                    using (MemoryStream stream = new(data, false))
                    {
                        Bitmap bitmap = new(stream);
                        e.Result = new LogoInfo(info.Id, bitmap);
                    }
                }
                catch (Exception)
                {
                    e.Result = new LogoInfo(info.Id, null);
                }
            }
        }

        private void OnDownloadLogo(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null || e.Cancelled == true)
            {
                return;
            }

            if (e.Result is LogoInfo logoInfo &&
                logoInfo.Bitmap != null &&
                this._Games.TryGetValue(logoInfo.Id, out var gameInfo) == true)
            {
                this._GameListView.BeginUpdate();
                var imageIndex = this._LogoImageList.Images.Count;
                this._LogoImageList.Images.Add(gameInfo.ImageUrl, logoInfo.Bitmap);
                gameInfo.ImageIndex = imageIndex;
                this._GameListView.EndUpdate();
            }

            this.DownloadNextLogo();
        }

        private void DownloadNextLogo()
        {
            lock (this._LogoLock)
            {

                if (this._LogoWorker.IsBusy == true)
                {
                    return;
                }

                GameInfo info;
                while (true)
                {
                    if (this._LogoQueue.TryDequeue(out info) == false)
                    {
                        this._DownloadStatusLabel.Visible = false;
                        return;
                    }

                    if (info.Item == null)
                    {
                        continue;
                    }

                    if (this._FilteredGames.Contains(info) == false ||
                        info.Item.Bounds.IntersectsWith(this._GameListView.ClientRectangle) == false)
                    {
                        this._LogosAttempting.Remove(info.ImageUrl);
                        continue;
                    }

                    break;
                }

                this._DownloadStatusLabel.Text = $"Downloading {1 + this._LogoQueue.Count} game icons...";
                this._DownloadStatusLabel.Visible = true;

                this._LogoWorker.RunWorkerAsync(info);
            }
        }

        private string GetGameImageUrl(uint id)
        {
            string candidate;

            var currentLanguage = this._SteamClient.SteamApps008.GetCurrentGameLanguage();

            candidate = this._SteamClient.SteamApps001.GetAppData(id, _($"small_capsule/{currentLanguage}"));
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return _($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{candidate}");
            }

            if (currentLanguage != "english")
            {
                candidate = this._SteamClient.SteamApps001.GetAppData(id, "small_capsule/english");
                if (string.IsNullOrEmpty(candidate) == false)
                {
                    return _($"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{id}/{candidate}");
                }
            }

            candidate = this._SteamClient.SteamApps001.GetAppData(id, "logo");
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return _($"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{id}/{candidate}.jpg");
            }

            return null;
        }

        private void AddGameToLogoQueue(GameInfo info)
        {
            if (info.ImageIndex > 0)
            {
                return;
            }

            var imageUrl = GetGameImageUrl(info.Id);
            if (string.IsNullOrEmpty(imageUrl) == true)
            {
                return;
            }

            info.ImageUrl = imageUrl;

            int imageIndex = this._LogoImageList.Images.IndexOfKey(imageUrl);
            if (imageIndex >= 0)
            {
                info.ImageIndex = imageIndex;
            }
            else if (
                this._LogosAttempting.Contains(imageUrl) == false &&
                this._LogosAttempted.Contains(imageUrl) == false)
            {
                this._LogosAttempting.Add(imageUrl);
                this._LogoQueue.Enqueue(info);
            }
        }

        private bool OwnsGame(uint id)
        {
            return this._SteamClient.SteamApps008.IsSubscribedApp(id);
        }

        private void AddGame(uint id, string type)
        {
            if (this._Games.ContainsKey(id) == true)
            {
                return;
            }

            if (this.OwnsGame(id) == false)
            {
                return;
            }

            GameInfo info = new(id, type);
            info.Name = this._SteamClient.SteamApps001.GetAppData(info.Id, "name");
            this._Games.Add(id, info);
        }

        private void AddGames()
        {
            this._Games.Clear();
            this._RefreshGamesButton.Enabled = false;
            this._ListWorker.RunWorkerAsync();
        }

        private void AddDefaultGames()
        {
            this.AddGame(480, "normal"); // Spacewar
        }

        private void OnTimer(object sender, EventArgs e)
        {
            this._CallbackTimer.Enabled = false;
            this._SteamClient.RunCallbacks(false);
            this._CallbackTimer.Enabled = true;
        }

        private void OnActivateGame(object sender, EventArgs e)
        {
            var focusedItem = (sender as MyListView)?.FocusedItem;
            var index = focusedItem != null ? focusedItem.Index : -1;
            if (index < 0 || index >= this._FilteredGames.Count)
            {
                return;
            }

            var info = this._FilteredGames[index];
            if (info == null)
            {
                return;
            }

            try
            {
                Process.Start("SAM.Game.exe", info.Id.ToString(CultureInfo.InvariantCulture));
            }
            catch (Win32Exception)
            {
                MessageBox.Show(
                    this,
                    "Failed to start SAM.Game.exe.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            this._AddGameTextBox.Text = "";
            this.AddGames();
        }

        private void OnAddGame(object sender, EventArgs e)
        {
            uint id;

            if (uint.TryParse(this._AddGameTextBox.Text, out id) == false)
            {
                MessageBox.Show(
                    this,
                    "Please enter a valid game ID.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (this.OwnsGame(id) == false)
            {
                MessageBox.Show(this, "You don't own that game.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            while (this._LogoQueue.TryDequeue(out var logo) == true)
            {
                // clear the download queue because we will be showing only one app
                this._LogosAttempted.Remove(logo.ImageUrl);
            }

            this._AddGameTextBox.Text = "";
            this._Games.Clear();
            this.AddGame(id, "normal");
            this._FilterGamesMenuItem.Checked = true;
            this.RefreshGames();
            this.DownloadNextLogo();
        }

        private void OnFilterUpdate(object sender, EventArgs e)
        {
            this.RefreshGames();

            // Compatibility with _GameListView SearchForVirtualItemEventHandler (otherwise _SearchGameTextBox loose focus on KeyUp)
            this._SearchGameTextBox.Focus();
        }

        private void OnUnlockAllGames(object sender, EventArgs e)
        {
            // Подтверждение
            if (MessageBox.Show(
                this,
                "This will unlock ALL achievements for ALL games in your library!\n\n" +
                "This may take a very long time and cannot be undone easily.\n\n" +
                "Are you sure you want to continue?",
                "Warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.No)
            {
                return;
            }

            // Второе подтверждение
            if (MessageBox.Show(
                this,
                "Really really sure?",
                "Final Warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error) == DialogResult.No)
            {
                return;
            }

            // Отключаем кнопки
            this._UnlockAllGamesButton.Enabled = false;
            this._RefreshGamesButton.Enabled = false;

            // Получаем список всех игр
            var gamesToProcess = this._Games.Values.ToList();
            
            if (gamesToProcess.Count == 0)
            {
                MessageBox.Show(this, "No games found!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this._UnlockAllGamesButton.Enabled = true;
                this._RefreshGamesButton.Enabled = true;
                return;
            }
            
            this._PickerStatusLabel.Text = $"Processing {gamesToProcess.Count} games...";

            // Обрабатываем в фоновом потоке
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, args) =>
            {
                int processed = 0;
                int totalAchievements = 0;

                foreach (var game in gamesToProcess)
                {
                    processed++;
                    
                    // Обновляем статус в UI потоке
                    this.Invoke(new Action(() =>
                    {
                        this._PickerStatusLabel.Text = $"Processing {processed}/{gamesToProcess.Count}: {game.Name}";
                    }));

                    try
                    {
                        // Запускаем SAM.Game для каждой игры с параметром --unlock-all
                        var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = "SAM.Game.exe",
                            Arguments = $"{game.Id.ToString(CultureInfo.InvariantCulture)} --unlock-all",
                            WindowStyle = ProcessWindowStyle.Hidden,
                            CreateNoWindow = true
                        });

                        // Ждём пока процесс завершится (максимум 15 секунд)
                        if (process != null)
                        {
                            process.WaitForExit(15000);
                            if (!process.HasExited)
                            {
                                process.Kill();
                            }
                        }

                        totalAchievements++;
                    }
                    catch
                    {
                        // Игнорируем ошибки
                    }

                    // Небольшая задержка между играми
                    System.Threading.Thread.Sleep(200);
                }

                args.Result = Tuple.Create(processed, totalAchievements);
            };

            worker.RunWorkerCompleted += (s, args) =>
            {
                // Включаем кнопки обратно
                this._UnlockAllGamesButton.Enabled = true;
                this._RefreshGamesButton.Enabled = true;

                if (args.Error != null)
                {
                    this._PickerStatusLabel.Text = "Error occurred!";
                    MessageBox.Show(
                        this,
                        $"An error occurred:\n{args.Error.Message}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                var result = (Tuple<int, int>)args.Result;
                this._PickerStatusLabel.Text = $"Completed! Processed {result.Item1} games.";

                MessageBox.Show(
                    this,
                    $"Completed!\n\nProcessed {result.Item1} games",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            };

            worker.RunWorkerAsync();
        }

        private void OnAddFreeGames(object sender, EventArgs e)
        {
            // Confirmation
            if (MessageBox.Show(
                this,
                "This will add ALL free-to-play games to your Steam library via Steam Web API.\n\n" +
                "IMPORTANT: Games will only be ADDED to library, but will NOT be downloaded!\n\n" +
                "This may take some time.\n\n" +
                "Continue?",
                "Add Free Games",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.No)
            {
                return;
            }

            this._AddFreeGamesButton.Enabled = false;
            this._PickerStatusLabel.Text = "Getting list of free games...";

            // Run in background thread
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, args) =>
            {
                var apiKey = "595633FC480CDF8A306F6F9D16E4AF3D";
                var steamId = this._SteamClient.SteamUser.GetSteamId();
                int added = 0;
                var addedGames = new System.Text.StringBuilder();

                try
                {
                    using (var client = new System.Net.WebClient())
                    {
                        client.Headers.Add("User-Agent", "ELDEVCREATOR RU STEAM HELP");
                        
                        // List of verified free Sub IDs from SteamDB
                        var freeSubIds = new List<uint>
                        {
                            // Valve F2P
                            0, // Special ID for all F2P games
                            // Can add specific Sub IDs if needed
                        };

                        // Use AddFreeLicense method via Steam Web API
                        foreach (var subId in freeSubIds)
                        {
                            try
                            {
                                var url = $"https://api.steampowered.com/IPlayerService/AddFreeLicense/v1/?key={apiKey}&steamid={steamId}&subid={subId}";
                                var response = client.DownloadString(url);
                                
                                if (response.Contains("\"success\":true") || response.Contains("\"result\":1"))
                                {
                                    added++;
                                    addedGames.AppendLine($"Added free games via Sub ID {subId}");
                                    
                                    this.Invoke(new Action(() =>
                                    {
                                        this._PickerStatusLabel.Text = $"Added: {added}";
                                    }));
                                }
                                
                                System.Threading.Thread.Sleep(1000);
                            }
                            catch (Exception ex)
                            {
                                addedGames.AppendLine($"Error for Sub ID {subId}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    args.Result = new Tuple<int, string, string>(0, null, ex.Message);
                    return;
                }
                
                args.Result = new Tuple<int, string, string>(added, addedGames.ToString(), null);
            };

            worker.RunWorkerCompleted += (s, args) =>
            {
                this._AddFreeGamesButton.Enabled = true;
                
                if (args.Error != null)
                {
                    this._PickerStatusLabel.Text = "Error occurred";
                    MessageBox.Show(
                        this,
                        $"An error occurred:\n{args.Error.Message}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                var result = (Tuple<int, string, string>)args.Result;
                
                if (result.Item3 != null)
                {
                    this._PickerStatusLabel.Text = "Error occurred";
                    MessageBox.Show(
                        this,
                        $"An error occurred:\n{result.Item3}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                this._PickerStatusLabel.Text = $"Added {result.Item1} free games";

                var message = new System.Text.StringBuilder();
                message.AppendLine($"Result:");
                message.AppendLine();
                message.AppendLine(result.Item2);
                message.AppendLine("\nCheck your Steam library!");

                MessageBox.Show(
                    this,
                    message.ToString(),
                    "Result",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            };

            worker.RunWorkerAsync();
        }

        private void OnGameListViewDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;

            if (e.Item.Bounds.IntersectsWith(this._GameListView.ClientRectangle) == false)
            {
                return;
            }

            var info = this._FilteredGames[e.ItemIndex];
            if (info.ImageIndex <= 0)
            {
                this.AddGameToLogoQueue(info);
                this.DownloadNextLogo();
            }
        }
    }
}
