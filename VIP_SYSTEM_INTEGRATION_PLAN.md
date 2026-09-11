# VRChat VIP System – Group Sync, GitHub Pages & SharedPrefabs Integration Plan

## 1. Prehľad a Zámer (Executive Summary)
Tento dokument slúži ako kompletný a vyčerpávajúci návod na implementáciu VIP systému pre VRChat svety.
Systém prepája **VRChat Group** (`grp_194a467e-b312-4596-89c2-c51c841ebb9e`), **GitHub Pages** repozitára `FoxDen` (`https://misosk.github.io/FoxDen/`), zdieľaný Unity balíček **SharedPrefabs** (`D:\Worlds\SharedPrefabs`) a samotnú scénu vo svete (`HomeByTheSea.unity`).

---

## 2. Prieskum a Presné Zistené Údaje (Pre-Reconnaissance)

### A. VRChat Udon Obmedzenia
- **Udon / U# natívne nepodporuje čítanie skupín a rolí:** VRChat SDK (ani najnovší SDK 3.10.2) nemá žiadne metódy v `VRCPlayerApi` na zistenie skupiny či roly hráča.
- **VRChat API endpoint:** Endpoint `https://vrchat.com/api/1/...` nie je možné volať priamo z hry cez `VRCStringDownloader`, pretože vyžaduje autorizačnú cookie / token a Udon nepovolí autorizačné hlavičky.
- **Riešenie:**
  1. **Editor:** V Unity Editori sa cez VRChat SDK prihlásenie (`APIUser.CurrentUser` a existujúce auth cookies) zavolá VRChat API.
  2. **Storage / Hosting:** Zoznamy sa uložia do `D:\Worlds\SharedPrefabs\files\` a cez Git pushnú na GitHub Pages:
     - `https://misosk.github.io/FoxDen/files/vip.txt`
     - `https://misosk.github.io/FoxDen/files/staff.txt`
  3. **Runtime:** V hre Udon skript použije `VRCStringDownloader.LoadUrl(...)` z týchto GitHub Pages adries, pričom lokálne súbory slúžia ako offline fallback.

### B. Balíček SharedPrefabs (`com.myname.sharedprefabs`)
- **Cesta na disku:** `D:\Worlds\SharedPrefabs`
- **Git Remote:** `https://github.com/misosk/FoxDen.git` (branch `main`)
- **Web URL (GitHub Pages):** `https://misosk.github.io/FoxDen/`
- **Konfigurácia v Unity:**
  - V `Packages/manifest.json`: `"com.myname.sharedprefabs": "file:D:/Worlds/SharedPrefabs"`
  - V `Packages/packages-lock.json`: `"com.myname.sharedprefabs": { "version": "file:D:/Worlds/SharedPrefabs", "depth": 0, "source": "local" }`
- **Súbory a GUIDs v SharedPrefabs:**
  - `D:\Worlds\SharedPrefabs\VIP Names.prefab`: GUID `488a839ed522a474d83109c168bbe133`
  - `D:\Worlds\SharedPrefabs\VIPTest.txt`: GUID `89a115a396048a84c82dea4a6d85e1b0`
  - `D:\Worlds\SharedPrefabs\StaffList.txt`: GUID `01e18e2208e0f874d9c3aa1914c3066e`
  - `D:\Worlds\SharedPrefabs\files\whitelist.txt`: GUID `whitelist.txt.meta`
  - Nové súbory pre export: `D:\Worlds\SharedPrefabs\files\vip.txt` a `D:\Worlds\SharedPrefabs\files\staff.txt`

### C. Existujúci stav v projekte `[HOME] 2.0 PC`
- **Skript VIPSystem:**
  - Cesta: `Assets/+ Misos Assets/Scripts/VIPSystem.cs` (GUID: `73c383ee04779a744bbb2cd0bb3d2e38`)
  - Program Asset: `Assets/+ Misos Assets/Scripts/VIPSystem.asset` (GUID: `d061dbe8182b1c54788598bc62e22d22`)
- **Aktuálny prefab:**
  - `Assets/+ Prefabs/VIPSystem.prefab` (GUID: `5e42fa15ffeac494a94ca63e9013ae39`)
- **Stav v scéne `HomeByTheSea.unity`:**
  - GameObject `VIPSystem` je v hierarchii scény rozbalený (unpacked):
    - GameObject ID: `8797954786766041244`
    - Transform ID: `5878815298117230970`
    - UdonBehaviour ID: `8797954786766041245` (program asset GUID: `c0d2ca8292809b649aef3b411b4cd8fa`)
    - U# Proxy ID: `8797954786766041246`
    - Parent Transform: `4549332498214353207` (pozícia: `x: 42.5, y: -0.0005341768, z: 10.785971`)
    - Child 1: `JoinSounds` (Transform: `2903318538875440323` -> obsahuje `VIPMemberJoinSound` a `StaffMemberJoinSound`)
    - Child 2: `VIP system` marker (Transform: `789750174`)
    - Child 3: `VIP names` (Transform: `639296550`, PrefabInstance: `639296549` z `VIP Names.prefab` GUID: `488a839ed522a474d83109c168bbe133`)
  - **Presné referencie na scéne (HomeByTheSea):**
    - `displayMemberStatus`: `{fileID: 5671297078655721931}` (TextMeshProUGUI v HOME UI)
    - `displayGuestNameUI`: `{fileID: 3404768139384754396}` (TextMeshProUGUI v HOME UI)
    - `displayNameUI`: `{fileID: 3404768139384754396}` (TextMeshProUGUI v HOME UI)
    - `StaffDisplayNameUI`: `{fileID: 3404768139384754396}` (TextMeshProUGUI v HOME UI)
    - `vipJoinSound`: `{fileID: 6584916030701409930}` (AudioSource)
    - `staffJoinSound`: `{fileID: 1006552514399084397}` (AudioSource)
    - `vipEnabledObjects`:
      - `{fileID: 94605596}`
      - `{fileID: 4072392678078219401}`
      - `{fileID: 817188853}`
      - `{fileID: 1327033386}`
      - `{fileID: 1268070754}`
    - `StaffEnabledObjects`:
      - `{fileID: 160655348}`
      - `{fileID: 289837392}`

---

## 3. Architektúra Riešenia

```
                    +--------------------------------------------------+
                    |  VRChat Group                                    |
                    |  grp_194a467e-b312-4596-89c2-c51c841ebb9e         |
                    |  (Roles: VIP, Staff)                             |
                    +------------------------+-------------------------+
                                             |
                                [Editor Tool Fetch]
                                             v
                    +--------------------------------------------------+
                    |  Unity Editor Tool (VRChatGroupVipSyncWindow.cs) |
                    |  - Fetch cez VRC SDK Session / API               |
                    |  - Pridá "[1] Local Player" a "[2] Local Player" |
                    |  - Uloží do D:\Worlds\SharedPrefabs\files\       |
                    |  - Git Commit & Push na GitHub                   |
                    +------------------------+-------------------------+
                                             |
                                       [git push]
                                             v
                    +--------------------------------------------------+
                    |  GitHub Pages (FoxDen)                           |
                    |  https://misosk.github.io/FoxDen/files/vip.txt   |
                    |  https://misosk.github.io/FoxDen/files/staff.txt |
                    +------------------------+-------------------------+
                                             |
                                 [VRCStringDownloader]
                                             v
+-----------------------------------------------------------------------------------------+
| In-Game Runtime (VIPSystem.cs v SharedPrefabs.prefab)                                  |
| 1. Štart: Inicializácia z lokálneho TextAssetu (Offline fallback).                      |
| 2. ClientSim overenie: Ak je "[1] Local Player" / "[2] Local Player", ihneď STAFF+VIP.  |
| 3. Online sync: VRCStringDownloader stiahne vip.txt a staff.txt z GitHub Pages.         |
| 4. Update stavu: Po stiahnutí prehodnotí stav lokálneho hráča a aktivuje objekty/zvuky.|
| 5. Event zmena: Po pridaní do skupiny stačí hráčovi Rejoin na svet!                    |
+-----------------------------------------------------------------------------------------+
```

---

## 4. Krok-za-krokom Inštrukcie pre Implementáciu

### KROK 1: Aktualizácia `VIPSystem.cs`
Vytvoriť/aktualizovať kód `VIPSystem.cs`:
1. **Podpora ClientSim:**
   ```csharp
   private bool IsLocalTestPlayer(string name)
   {
       if (string.IsNullOrEmpty(name)) return false;
       return name == "[1] Local Player" || name == "[2] Local Player" || name.StartsWith("[");
   }
   ```
2. **Podpora GitHub Pages URL a VRCStringDownloader:**
   ```csharp
   [Header("Remote Sync (GitHub Pages)")]
   public bool useRemoteUrls = true;
   public VRCUrl remoteVipUrl = new VRCUrl("https://misosk.github.io/FoxDen/files/vip.txt");
   public VRCUrl remoteStaffUrl = new VRCUrl("https://misosk.github.io/FoxDen/files/staff.txt");
   public float retryDelay = 10f;
   ```
3. **Logika vyhodnotenia pri štarte a po stiahnutí:**
   - V `Start()`:
     ```csharp
     if (Utilities.IsValid(vipList)) vipNames = vipList.text.Split('\n');
     if (Utilities.IsValid(StaffList)) StaffNames = StaffList.text.Split('\n');
     EvaluatePlayer();
     if (useRemoteUrls)
     {
         if (remoteVipUrl != null && !string.IsNullOrEmpty(remoteVipUrl.Get()))
             VRCStringDownloader.LoadUrl(remoteVipUrl, (UdonBehaviour)(Component)this);
         if (remoteStaffUrl != null && !string.IsNullOrEmpty(remoteStaffUrl.Get()))
             VRCStringDownloader.LoadUrl(remoteStaffUrl, (UdonBehaviour)(Component)this);
     }
     ```
   - V `EvaluatePlayer()`:
     - Ak `IsLocalTestPlayer(displayName)` -> `UpdateStateNitro(displayName); UpdateStateVIP(displayName);` (má obe roly).
     - Inak overenie cez `ArrayContainsString`.
   - V `OnStringLoadSuccess(IVRCStringDownload result)`:
     - Identifikácia podľa URL, rozdelenie reťazca cez `\r\n` a `\n`.
     - Opätovné zavolanie `EvaluatePlayer()`.
   - V `OnStringLoadError(IVRCStringDownload result)`:
     - Logovanie chyby a ponechanie offline zoznamu.

### KROK 2: Umiestnenie `VIPSystem.prefab` do `D:\Worlds\SharedPrefabs`
1. Vytvoriť v `D:\Worlds\SharedPrefabs\VIPSystem.prefab` čistý prefab:
   - Root: `VIPSystem` (obsahuje UdonBehaviour + VIPSystem U# script).
   - Odkazy na lokálne súbory: `D:\Worlds\SharedPrefabs\files\vip.txt` (alebo `VIPTest.txt`) a `StaffList.txt`.
   - Child objekty:
     - `JoinSounds` (s `VIPMemberJoinSound` a `StaffMemberJoinSound`).
     - `VIP names` (inštancia prefabu `D:\Worlds\SharedPrefabs\VIP Names.prefab`).
     - `VIP system` (marker objekt).
2. Týmto je prefab plne autonómny a použiteľný v akomkoľvek projekte obsahujúcom package `com.myname.sharedprefabs`.

### KROK 3: Výmena GameObjektu v scéne `HomeByTheSea.unity`
1. V scéne nahradiť rozbalený objekt `VIPSystem` (ID `8797954786766041244`) za inštanciu nového prefabu `D:\Worlds\SharedPrefabs\VIPSystem.prefab` pod otcom `4549332498214353207`.
2. Do inštancie preniesť špecifické scéne väzby (Prefab Modifications):
   - `displayMemberStatus` -> `{fileID: 5671297078655721931}`
   - `displayGuestNameUI` -> `{fileID: 3404768139384754396}`
   - `displayNameUI` -> `{fileID: 3404768139384754396}`
   - `StaffDisplayNameUI` -> `{fileID: 3404768139384754396}`
   - `vipEnabledObjects` -> `[94605596, 4072392678078219401, 817188853, 1327033386, 1268070754]`
   - `StaffEnabledObjects` -> `[160655348, 289837392]`

### KROK 4: Unity Editor Tool (`VRChatGroupVipSyncWindow.cs`)
Vytvoriť skript v `D:\Worlds\SharedPrefabs\Editor\VRChatGroupVipSyncWindow.cs` (alebo v projekte pod `Editor/`):
- **Menu položka:** `VRChat Tools > VIP Group Sync`
- **Konštanty:**
  - Group ID: `grp_194a467e-b312-4596-89c2-c51c841ebb9e`
  - VIP Role Name: `VIP`
  - Staff Role Name: `Staff`
  - Výstupné cesty:
    - `D:\Worlds\SharedPrefabs\files\vip.txt`
    - `D:\Worlds\SharedPrefabs\files\staff.txt`
    - `D:\Worlds\SharedPrefabs\VIPTest.txt` (spätná kompatibilita)
    - `D:\Worlds\SharedPrefabs\StaffList.txt` (spätná kompatibilita)
- **Logika sťahovania z VRChat API:**
  - Zavolá VRChat API: `GET https://vrchat.com/api/1/groups/grp_194a467e-b312-4596-89c2-c51c841ebb9e/roles` na zistenie Role ID pre roly "VIP" a "Staff".
  - Zavolá `GET https://vrchat.com/api/1/groups/grp_194a467e-b312-4596-89c2-c51c841ebb9e/members?roleId={roleId}&n=100`.
  - Vyextrahuje `user.displayName`.
  - Vždy vloží na začiatok:
    ```
    [1] Local Player
    [2] Local Player
    ```
  - Zapíše UTF-8 súbory do repozitára.
- **Tlačidlo Git Push:**
  - Vykoná proces `git` v adresári `D:\Worlds\SharedPrefabs`:
    ```powershell
    git add files/vip.txt files/staff.txt VIPTest.txt StaffList.txt
    git commit -m "Update VIP and Staff member lists from VRChat Group"
    git push origin main
    ```
  - Zobrazí v editore notifikáciu s odkazom na GitHub Pages.

---

## 5. Overenie a Kontrolný Zoznam (Verification Checklist)
1. **Editor Test:** V Play Mode (ClientSim) overiť, že `[1] Local Player` má magenta/cyan status, zvuky reagujú a VIP/Staff objekty sú zapnuté.
2. **Git & GitHub Pages Test:**
   - Overiť `curl -I https://misosk.github.io/FoxDen/files/vip.txt` (HTTP 200 OK).
3. **Rejoin Test:** V živom svete po pridaní hráča do skupiny a stlačení Sync tlačidla stačí hráčovi rejoinnúť inštanciu a je automaticky rozpoznaný ako VIP/Staff.
