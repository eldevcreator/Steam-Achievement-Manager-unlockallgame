# Как собрать через AppVeyor

## Вариант 1: Использовать AppVeyor (автоматическая сборка)

### Шаг 1: Создать репозиторий на GitHub

1. Зайди на https://github.com/new
2. Создай новый репозиторий (например `SAM-UnlockAll`)
3. **НЕ** инициализируй с README

### Шаг 2: Запушить код

```bash
# В папке с SAM выполни:
git init
git add .
git commit -m "Added Unlock All Games button"
git branch -M master
git remote add origin https://github.com/ТВОЙ_ЮЗЕРНЕЙМ/SAM-UnlockAll.git
git push -u origin master
```

### Шаг 3: Подключить AppVeyor

1. Зайди на https://ci.appveyor.com/
2. Войди через GitHub
3. Нажми **"New Project"**
4. Выбери свой репозиторий `SAM-UnlockAll`
5. AppVeyor автоматически найдет `.appveyor.yml` и начнет сборку

### Шаг 4: Скачать собранные файлы

1. После сборки зайди в **Artifacts**
2. Скачай `SAM.zip` или отдельные `.exe` файлы

---

## Вариант 2: Собрать локально (быстрее)

### Через Visual Studio

1. Открой `SAM.sln`
2. Выбери **Release | x86**
3. **Build > Build Solution** (Ctrl+Shift+B)
4. Файлы в `SAM.Picker\bin\x86\Release\SAM.Picker.exe`

### Через командную строку

```bash
# Если установлен Visual Studio
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" SAM.sln /p:Configuration=Release /p:Platform=x86

# Или через Developer Command Prompt
msbuild SAM.sln /p:Configuration=Release /p:Platform=x86
```

---

## Вариант 3: Использовать оригинальный AppVeyor (без изменений)

Если хочешь просто скачать оригинальный SAM без твоих изменений:

1. Зайди на https://ci.appveyor.com/project/gibbed/steamachievementmanager/branch/master
2. Нажми на последний успешный build (зеленая галочка)
3. Перейди в **Artifacts**
4. Скачай файлы

**НО** там не будет кнопки "Unlock All Games" - это только оригинальная версия!

---

## Что нужно для сборки локально

### Установить Visual Studio

1. Скачай **Visual Studio 2022 Community** (бесплатно):
   https://visualstudio.microsoft.com/downloads/

2. При установке выбери:
   - ✅ **.NET desktop development**
   - ✅ **Desktop development with C++** (опционально)

3. После установки открой `SAM.sln` и жми Build

---

## Быстрый способ (если есть Visual Studio)

```bash
# Открой Developer Command Prompt for VS 2022
# Перейди в папку с SAM
cd путь\к\SAM

# Собери
msbuild SAM.sln /p:Configuration=Release /p:Platform=x86

# Готово! Файлы в:
# SAM.Picker\bin\x86\Release\SAM.Picker.exe
# SAM.Game\bin\x86\Release\SAM.Game.exe
```

---

## Проверка изменений

После сборки запусти `SAM.Picker.exe` и выбери любую игру.

В открывшемся окне должна быть кнопка **"Unlock All Games"** в тулбаре (между Reset и Commit Changes).

Если кнопки нет - значит сборка была из оригинального кода без изменений.
