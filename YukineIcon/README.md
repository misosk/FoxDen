# YukineIcon

Zdieľaný VRChat/UdonSharp prefab ikonky, ktorú určený používateľ môže chytiť,
nosiť pri hlave a odložiť späť na označené miesto na bare. Po pustení v dosahu
`ShelfAnchor` sa ikonka na miesto zacvakne a zostane tam, kým ju používateľ
znovu nechytí.

Prefab je súčasťou lokálneho UPM balíka `com.myname.sharedprefabs`, takže jednu
verziu používajú všetky svety, ktoré majú v `Packages/manifest.json`:

```json
"com.myname.sharedprefabs": "file:D:/Worlds/SharedPrefabs"
```

## Štruktúra

```text
YukineIcon/
├─ YukineIcon.prefab
│  ├─ Follower                 pohyblivá ikonka/model
│  ├─ GrabHandle               VRC_Pickup a collider
│  └─ ShelfAnchor              pevné miesto na bare
│     └─ ShelfHotspot
│        ├─ SnapRing           kruhový marker
│        └─ DropTransparentPNGHere  voľný SpriteRenderer pre vlastné PNG
├─ Runtime/
│  ├─ YukineHeadIcon.cs
│  └─ Misos.YukineIcon.Runtime.asmdef
├─ Editor/
│  ├─ YukineHotspotBuilder.cs
│  └─ Misos.YukineIcon.Editor.asmdef
├─ YukineHotspotRing.asset     generovaný mesh kruhu
└─ YukineHotspotRing.mat       materiál kruhu
```

`ShelfHotspot` musí zostať pod `ShelfAnchor`, nie pod `Follower`. Vďaka tomu
marker zostane na bare, keď hráč ikonku odnesie.

## Runtime správanie

Hlavný komponent je `Runtime/YukineHeadIcon.cs`.

- `targetDisplayNames` určuje, komu ikonka patrí. Meno sa porovnáva bez ohľadu
  na veľkosť písmen. Dávaj pozor na Unicode znaky, napríklad `Yukıne` obsahuje
  turecké bezbodkové `ı`.
- Pickup je lokálne aktívny iba pre cieľového používateľa.
- Ak cieľový používateľ nie je v inštancii, ikonka sa vráti na `ShelfAnchor`.
- Počas nosenia sa poloha počíta z kosti hlavy avatara a z používateľského
  offsetu.
- Pri pustení sa poloha pickup handle porovná so `ShelfAnchor`. Ak je bližšie
  než `shelfSnapDistance` (aktuálne `0.3 m`), nastaví sa docked stav.
- `_syncedOffset` a `_syncedDocked` sú `UdonSynced`, takže ostatní hráči vidia
  rovnakú polohu a stav odloženia.
- Ak `shelfAnchor` chýba, skript použije štartovaciu polohu followera ako
  fallback. V hotovom prefabe však musí byť referencia vždy vyplnená.

## Vloženie vlastného transparentného PNG

Vnútri kruhu je ľahký `SpriteRenderer` s názvom `DropTransparentPNGHere`.
Nemá prednastavený sprite, takže prefab zobrazuje iba čistý kruh.

1. Importuj PNG s priehľadným pozadím.
2. V Import Settings nastav `Texture Type` na `Sprite (2D and UI)` a stlač
   `Apply`.
3. Otvor prefab a pretiahni sprite do poľa `Sprite` na objekte
   `DropTransparentPNGHere`.
4. Uprav jeho `Scale`, aby sa celý obrázok zmestil dovnútra kruhu. Objekt je už
   otočený do roviny baru a má vyššie sorting order než kruh.

Veľkosť kruhu sa nastavuje v `Editor/YukineHotspotBuilder.cs`:

```csharp
const float innerRadius = 0.205f;
const float outerRadius = 0.225f;
```

Po každej zmene generátora:

1. Nechaj Unity dokončiť kompiláciu.
2. Spusť `Misos > Yukine Icon > Rebuild Shelf Hotspot`.
3. Skontroluj, že `YukineIcon.prefab` obsahuje
   `ShelfAnchor/ShelfHotspot/SnapRing` a `DropTransparentPNGHere`.
4. Skontroluj Unity Console; nesmú zostať compilation errors.
5. Commitni aj generované `.asset`, `.mat` a `.meta` súbory a vlastné PNG.

## Vytvorenie ikonky pre ďalšieho používateľa

Najjednoduchší postup:

1. Skopíruj celý priečinok `YukineIcon` a premenuj ho podľa používateľa.
2. Premenuj prefab a nahraď model pod `Follower`. Zachovaj root objekty
   `Follower`, `GrabHandle` a `ShelfAnchor`.
3. V komponente nastav `targetDisplayNames` na presné VRChat meno a prípadné
   alternatívy mena.
4. Posuň `GrabHandle` a jeho collider do prirodzeného miesta uchopenia. Podľa
   potreby uprav `handleLocalOffset`, `defaultHeadOffset` a
   `maxOffsetDistance`.
5. V skopírovanom editorovom generátore zmeň všetky asset cesty, názov menu a
   názov triedy. Každá ikonka musí generovať vlastný ring a materiál, aby si
   navzájom neprepisovali assety. Do PNG slotu potom priraď obrázok používateľa.
6. Spusť nový rebuild príkaz a skontroluj výslednú prefab hierarchiu.
7. Otestuj pickup, pustenie mimo kruhu, zacvaknutie v kruhu a opätovné
   odlepenie. Nakoniec over synchronizáciu aspoň s dvoma klientmi.

Ak bude podobných ikoniek viac, ďalší vhodný refaktor je premenovať
`YukineHeadIcon` na všeobecný `ShelfHeadIcon` a prerobiť builder na jeden
konfigurovateľný nástroj namiesto kopírovania triedy.

## Dôležité zistenia a časté chyby

- Samotné pole `Shelf Anchor` v Inspectore nestačí. V prefabe musí existovať
  reálny transform `ShelfAnchor` a referencia nesmie zostať `None`.
- Editor assembly musí referencovať `Misos.YukineIcon.Runtime`,
  `UdonSharp.Runtime`. Bez `UdonSharp.Runtime` builder
  neskompiluje prístup ku komponentu odvodenému z `UdonSharpBehaviour`.
- Automatický `[InitializeOnLoadMethod]` vytvorí chýbajúci hotspot, ale pri
  zmene existujúceho vzhľadu použi vždy manuálny rebuild z menu.
- Zmeny v lokálnom UPM balíku sa niekedy načítajú až po `AssetDatabase.Refresh`
  alebo po prepnutí späť do Unity.
- Po verejnej zmene balíka zvýš verziu v koreňovom `package.json`, aby bola
  aktualizácia jasne rozpoznateľná vo všetkých svetoch.
- Neupravuj iba inštanciu prefabu v jednej scéne. Zdroj pravdy je
  `D:/Worlds/SharedPrefabs/YukineIcon/YukineIcon.prefab`.

## Kontrolný zoznam pred použitím

- `ShelfAnchor` je child rootu a je priradený v komponente.
- `ShelfHotspot` je child `ShelfAnchor`.
- Ring má platný mesh a materiál; PNG slot má `SpriteRenderer`.
- `GrabHandle` má `VRC_Pickup` a collider.
- Runtime assembly a UdonSharp program sa skompilovali bez chýb.
- Ikonka sa mimo snap dosahu vráti k hlave, v snap dosahu zostane na bare a
  po ďalšom grabe sa odlepí.
- Zmena je urobená v `SharedPrefabs`, nie iba v jednom world projekte.
