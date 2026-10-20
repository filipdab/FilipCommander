# FilipCommander

Dwupanelowy menedżer plików dla Windows w stylu Total Commandera.
C# / WinForms / .NET 8.

## Budowanie

Wymagany .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`).

```
dotnet run                         # uruchom
build.bat                          # jeden plik FilipCommander.exe w .\publish
```

`build.bat` robi samodzielny (self-contained) plik .exe — działa bez zainstalowanego .NET.

## Co umie

- dwa panele z ikonami systemowymi, wyborem dysku i wolnym miejscem
- sortowanie po kliknięciu w kolumnę (katalogi zawsze na górze, sortowanie naturalne: plik2 < plik10)
- zaznaczanie jak w TC (Insert, Spacja, prawy przycisk, maski Num+/Num−, Num*)
- Spacja na katalogu liczy jego rozmiar
- kopiowanie/przenoszenie/usuwanie przez powłokę Windows — systemowe okno postępu, pytania o nadpisanie, kosz
- podgląd F3: tekst (UTF-8 / UTF-16 / Windows-1250), hex dla plików binarnych, obrazy
- wyszukiwanie plików (Alt+F7): maski, tekst w środku pliku (UTF-8/UTF-16/Windows-1250), rozmiar, data; wyniki na żywo, „Przejdź do pliku”
- pakowanie do ZIP (Alt+F5) i rozpakowywanie (Alt+F9) z oknem postępu i anulowaniem; F3 na .zip pokazuje zawartość
- automatyczne odświeżanie po zmianach na dysku
- wiersz poleceń na dole (pisanie w panelu trafia do niego; `cd`, `D:` zmieniają katalog panelu)
- zapamiętywanie katalogów, rozmiaru okna i ustawień w `%AppData%\FilipCommander\settings.json`

## Skróty

| Klawisz | Akcja |
|---|---|
| Tab | przełącz panel |
| Enter / dwuklik | wejdź do katalogu / uruchom plik |
| Backspace, Ctrl+PgUp | katalog wyżej |
| Ctrl+\ | katalog główny dysku |
| Insert / Spacja / PPM | zaznacz |
| Num+ / Num− / Num* / Ctrl+A | zaznacz maską / odznacz maską / odwróć / wszystko |
| F3 | podgląd (H – hex, W – zawijanie, Esc – zamknij) |
| F4 / Shift+F4 | edycja / nowy plik tekstowy |
| F5 / F6 | kopiuj / przenieś |
| Shift+F6 | zmień nazwę |
| F7 | nowy folder (można podać `a\b\c`) |
| F8, Del / Shift+Del | do kosza / usuń trwale |
| Alt+Enter | właściwości |
| Alt+F7 | szukaj plików |
| Alt+F5 | spakuj zaznaczone do ZIP |
| Alt+F9 | rozpakuj zaznaczone archiwa ZIP |
| Alt+F1 / Alt+F2 | wybór dysku lewy / prawy |
| Ctrl+R | odśwież |
| Ctrl+H | pokaż ukryte/systemowe |
| Ctrl+U | zamień panele |
| Ctrl+E | ten sam katalog w drugim panelu |
| Ctrl+← / Ctrl+→ | katalog spod kursora w lewym / prawym panelu |
| Ctrl+T | cmd w bieżącym katalogu |
| Ctrl+O | pokaż w Eksploratorze |
| Ctrl+Enter | nazwa pliku do wiersza poleceń |
| F1 | pomoc |

Edytor dla F4 ustawisz w menu Widok → Edytor dla F4 (domyślnie notepad.exe).

## Pliki

| Plik | Zawartość |
|---|---|
| `MainForm.cs` | okno główne, menu, skróty, polecenia F3–F8, wiersz poleceń |
| `FilePanel.cs` | panel plików: lista, sortowanie, zaznaczanie, dyski, obserwator zmian |
| `SearchForm.cs` | okno wyszukiwania |
| `Archive.cs` | pakowanie/rozpakowywanie ZIP, okno postępu |
| `FileOps.cs` | kopiowanie/przenoszenie/usuwanie przez powłokę Windows |
| `Dialogs.cs` | okno pytania i podgląd F3 |
| `AboutForm.cs` | okno „O programie” |
| `ShellIcons.cs` | ikony systemowe i okno Właściwości (WinAPI) |
| `Settings.cs` | zapis/odczyt ustawień |

## ZIP — szczegóły

- Pakowanie zachowuje strukturę katalogów (ścieżki względem bieżącego katalogu panelu), puste katalogi i daty plików. Archiwum powstaje jako plik tymczasowy, więc przerwane pakowanie nie zostawia uszkodzonego .zip.
- Rozpakowywanie domyślnie trafia do podkatalogu o nazwie archiwum w drugim panelu. Jeśli pliki już istnieją, pyta: nadpisać, pominąć czy przerwać.
- Pozycje ze ścieżką wychodzącą poza katalog docelowy (`../`, „zip slip”) są pomijane.
- Polskie nazwy działają zarówno w archiwach UTF-8, jak i starszych z Eksploratora Windows (CP852).

## Autor

Filip Dąbrowski — filip@dabrowski.in — www.dabrowski.in
