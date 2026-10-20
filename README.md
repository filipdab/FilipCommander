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
- zakładki w panelach (Ctrl+T / Ctrl+W / Ctrl+Tab; środkowy przycisk zamyka, dwuklik na pustym pasku otwiera nową)
- historia katalogów (Alt+← / Alt+→, lista Alt+↓) i ulubione katalogi (Ctrl+D, z Pulpitem, Dokumentami i Pobranymi)
- sortowanie po kliknięciu w kolumnę (katalogi zawsze na górze, sortowanie naturalne: plik2 < plik10)
- widoki: szczegóły (Ctrl+F2), krótki — same nazwy w kolumnach (Ctrl+F1), miniatury jak w Eksploratorze (Ctrl+Shift+F1)
- szybkie wyszukiwanie w panelu (Ctrl+S, ↑/↓ — poprzedni/następny pasujący) i filtr (Ctrl+F, np. `*.cs` albo fragment nazwy)
- zaznaczanie jak w TC (Insert, Spacja, prawy przycisk, maski Num+/Num−, Num*)
- Spacja na katalogu liczy jego rozmiar
- kopiowanie/przenoszenie/usuwanie przez powłokę Windows — systemowe okno postępu, pytania o nadpisanie, kosz
- schowek zgodny z Eksploratorem (Ctrl+C / Ctrl+X / Ctrl+V) i kopiowanie pełnych ścieżek (Ctrl+Shift+C)
- przeciąganie plików między panelami i z/do Eksploratora (Ctrl — kopiuj, Shift — przenieś)
- menu kontekstowe Windows (Shift+F10 / klawisz menu; opcjonalnie prawym przyciskiem — Widok)
- zmiana nazw wielu plików (Ctrl+M): maski `[N]`, `[N2-5]`, `[E]`, `[C]`, `[P]`, `[YMD]`, `[hms]`, szukaj/zamień (też regex), wielkość liter, podgląd, cofanie
- porównanie katalogów (Shift+F2) — zaznacza nowsze i brakujące pliki; synchronizacja katalogów z podkatalogami (Polecenia → Synchronizuj)
- porównanie plików linia po linii (Plik → Porównaj pliki), binarne — bajt po bajcie
- szybki podgląd w drugim panelu (Ctrl+Q)
- podgląd F3: tekst (UTF-8 / UTF-16 / Windows-1250), hex dla plików binarnych, obrazy, zawartość archiwów
- wyszukiwanie plików (Alt+F7): maski, tekst w środku pliku (UTF-8/UTF-16/Windows-1250), rozmiar, data; wyniki na żywo, „Przejdź do pliku”
- archiwa: pakowanie do ZIP, TAR, TAR.GZ, TAR.BZ2 (Alt+F5); rozpakowywanie ZIP, 7z, RAR, TAR, TAR.GZ/BZ2/XZ, GZ (Alt+F9)
- sumy kontrolne MD5/SHA-1/SHA-256/SHA-512: obliczanie (format `sha256sum`) i sprawdzanie
- dzielenie dużych plików na części `.001`, `.002`… i łączenie ich z powrotem
- zmiana atrybutów i dat (także rekurencyjnie)
- motyw jasny/ciemny/systemowy i wybór czcionki paneli (menu Widok)
- automatyczne odświeżanie po zmianach na dysku
- wiersz poleceń na dole (pisanie w panelu trafia do niego; `cd`, `D:`, `cd ~` zmieniają katalog panelu)
- zapamiętywanie katalogów, zakładek, widoków, rozmiaru okna i ustawień w `%AppData%\FilipCommander\settings.json`

## Skróty

| Klawisz | Akcja |
|---|---|
| Tab | przełącz panel |
| Enter / dwuklik | wejdź do katalogu / uruchom plik |
| Backspace, Ctrl+PgUp | katalog wyżej |
| Ctrl+\ | katalog główny dysku |
| Alt+← / Alt+→ / Alt+↓ | wstecz / dalej / historia katalogów |
| Ctrl+D | ulubione katalogi |
| Ctrl+T / Ctrl+W / Ctrl+Tab | nowa / zamknij / następna zakładka |
| Insert / Spacja / PPM | zaznacz |
| Num+ / Num− / Num* / Ctrl+A | zaznacz maską / odznacz maską / odwróć / wszystko |
| Ctrl+S / Ctrl+F | szybkie wyszukiwanie / filtr (Esc wyłącza) |
| Shift+F2 | porównaj katalogi |
| F3 | podgląd (H – hex, W – zawijanie, Esc – zamknij) |
| Ctrl+Q | szybki podgląd w drugim panelu |
| F4 / Shift+F4 | edycja / nowy plik tekstowy |
| F5 / F6 | kopiuj / przenieś |
| Shift+F6 / Ctrl+M | zmień nazwę / zmień nazwy wielu plików |
| F7 | nowy folder (można podać `a\b\c`) |
| F8, Del / Shift+Del | do kosza / usuń trwale |
| Ctrl+C / Ctrl+X / Ctrl+V | kopiuj / wytnij / wklej (schowek Windows) |
| Ctrl+Shift+C | kopiuj pełne ścieżki |
| Shift+F10, klawisz menu | menu kontekstowe Windows |
| Alt+Enter | właściwości |
| Alt+F7 | szukaj plików |
| Alt+F5 | spakuj zaznaczone (ZIP, TAR, TAR.GZ, TAR.BZ2 — wg rozszerzenia) |
| Alt+F9 | rozpakuj zaznaczone archiwa (ZIP, 7z, RAR, TAR…) |
| Ctrl+F1 / Ctrl+F2 / Ctrl+Shift+F1 | widok krótki / szczegóły / miniatury |
| Alt+F1 / Alt+F2 | wybór dysku lewy / prawy |
| Ctrl+R | odśwież |
| Ctrl+H | pokaż ukryte/systemowe |
| Ctrl+U | zamień panele |
| Ctrl+E | ten sam katalog w drugim panelu |
| Ctrl+← / Ctrl+→ | katalog spod kursora w lewym / prawym panelu |
| Ctrl+Shift+T | cmd w bieżącym katalogu |
| Ctrl+O | pokaż w Eksploratorze |
| Ctrl+Enter | nazwa pliku do wiersza poleceń |
| F1 | pomoc |

Bez skrótu (menu Plik): atrybuty i daty, sumy kontrolne, dzielenie/łączenie plików, porównanie plików; (menu Polecenia): synchronizacja katalogów.

Edytor dla F4 ustawisz w menu Widok → Edytor dla F4 (domyślnie notepad.exe).

## Pliki

| Plik | Zawartość |
|---|---|
| `MainForm.cs` | okno główne, menu, skróty, polecenia, schowek, wiersz poleceń |
| `FilePanel.cs` | panel plików: lista, zakładki, historia, sortowanie, zaznaczanie, szybkie wyszukiwanie/filtr, widoki i miniatury, przeciąganie, szybki podgląd, dyski, obserwator zmian |
| `SearchForm.cs` | okno wyszukiwania |
| `Archive.cs` | pakowanie/rozpakowywanie ZIP, okno postępu |
| `ArchiveOps.cs` | 7z, RAR, TAR, GZ… przez SharpCompress |
| `FileOps.cs` | kopiowanie/przenoszenie/usuwanie przez powłokę Windows |
| `Dialogs.cs` | okno pytania, okno tekstu/raportu, podgląd (kontrolka i okno F3) |
| `MultiRenameForm.cs` | zmiana nazw wielu plików |
| `DiffForm.cs` | porównanie plików (algorytm Myersa) |
| `SyncForm.cs` | synchronizacja katalogów |
| `Tools.cs` | sumy kontrolne, dzielenie/łączenie plików, atrybuty i daty |
| `ShellContextMenu.cs` | menu kontekstowe Eksploratora (IContextMenu) |
| `Theme.cs` | motyw jasny/ciemny |
| `AboutForm.cs` | okno „O programie” |
| `ShellIcons.cs` | ikony systemowe, miniatury, okno Właściwości, katalog Pobrane (WinAPI) |
| `Settings.cs` | zapis/odczyt ustawień |

## ZIP — szczegóły

- Pakowanie zachowuje strukturę katalogów (ścieżki względem bieżącego katalogu panelu), puste katalogi i daty plików. Archiwum powstaje jako plik tymczasowy, więc przerwane pakowanie nie zostawia uszkodzonego .zip.
- Rozpakowywanie domyślnie trafia do podkatalogu o nazwie archiwum w drugim panelu. Jeśli pliki już istnieją, pyta: nadpisać, pominąć czy przerwać.
- Pozycje ze ścieżką wychodzącą poza katalog docelowy (`../`, „zip slip”) są pomijane.
- Polskie nazwy działają zarówno w archiwach UTF-8, jak i starszych z Eksploratora Windows (CP852).

## Autor

Filip Dąbrowski — filip@dabrowski.in — www.dabrowski.in
