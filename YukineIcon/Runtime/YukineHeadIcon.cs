using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Animovaná ikonka, ktorá stojí na poličke a vznáša sa nad hlavou konkrétneho hráča,
/// keď je v inštancii. Cieľový hráč si ju vie chytiť za neviditeľný pickup v bruchu
/// a posunúť relatívne k svojej hlave; offset sa synchronizuje ostatným. Keď ju
/// pustí pri jej mieste na poličke, ikonka sa tam zacvakne a zostane odložená,
/// kým ju cieľový hráč znovu nechytí.
///
/// Pozícia hlavy sa číta z KOSTI hlavy avatara (GetBonePosition), nie z tracking dát.
/// Kosť je tam, kde je avatar naozaj vykreslený na danom klientovi, takže to sedí aj
/// keď si hráč cez OVR Advanced Settings nadvihne playspace - vtedy kapsula hráča
/// ostáva na zemi, ale avatar je vyššie a tracking dáta to pre ostatných neodrážajú.
///
/// Nič sa nesynchronizuje okrem offsetu: cieľ si počíta každý klient sám zo svojho
/// vlastného vykreslenia avatara. Rotácia hlavy sa zámerne nepoužíva.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
[AddComponentMenu("Misos/Yukine Head Icon")]
public class YukineHeadIcon : UdonSharpBehaviour
{
    [Header("Komu ikonka patrí")]
    [Tooltip("Presné VRChat display mená. Pozor: Yukine sa píše s dotless i (U+0131).")]
    public string[] targetDisplayNames = new string[] { "Yukıne" };

    [Tooltip("Len pre test v ClientSim / Play mode - lokálny hráč sa berie ako cieľ.")]
    public bool debugTreatLocalPlayerAsTarget = false;

    [Header("Referencie")]
    [Tooltip("Objekt, ktorý sa reálne hýbe (obsahuje Model s Animatorom).")]
    public Transform follower;

    [Tooltip("Miesto na poličke, kde ikonka stojí. Ak nie je nastavené, použije sa jej štartovacia poloha.")]
    public Transform shelfAnchor;

    [Tooltip("Capsule collider + VRC_Pickup. Aktívny len na klientovi cieľového hráča.")]
    public GameObject grabHandle;

    [Tooltip("VRC_Pickup na grabHandle.")]
    public VRC_Pickup pickup;

    [Header("Zdroj pozície hlavy")]
    [Tooltip("0 = Auto (kosť hlavy -> avatar root -> tracking -> kapsula). " +
             "1 = len kosť hlavy. 2 = len tracking dáta (staré správanie). " +
             "3 = avatar root + výška očí. Ak by Auto niekde zlyhalo, prepni tu bez rebuildu.")]
    public int headSource = 0;

    [Tooltip("Vypisuje do konzoly všetky zdroje pozície, nech vidno ktorý sedí. Nechaj vypnuté.")]
    public bool debugHeadSources = false;

    [Header("Pozicovanie")]
    [Tooltip("Východiskový posun voči hlave, kým si ho hráč neposunie sám.")]
    public Vector3 defaultHeadOffset = new Vector3(0f, 0.45f, 0f);

    [Tooltip("Východzí offset sa prepočíta podľa výšky avatara (malá postava = menší odstup). " +
             "Len pre východziu hodnotu - keď si ikonku raz potiahneš, platí tvoj offset.")]
    public bool scaleDefaultOffsetWithAvatar = true;

    [Tooltip("Ako ďaleko od hlavy sa dá ikonka odtiahnuť.")]
    public float maxOffsetDistance = 1.5f;

    [Tooltip("Kde na ikonke sedí úchyt (v lokálnom priestore followera) - cca brucho.")]
    public Vector3 handleLocalOffset = new Vector3(0f, 0.15f, 0f);

    [Header("Odloženie na poličku")]
    [Tooltip("Ako blízko k shelfAnchor treba ikonku pustiť, aby sa na miesto zacvakla.")]
    public float shelfSnapDistance = 0.3f;

    [Header("Vyhladenie")]
    [Tooltip("Čas dobehnutia nad hlavu. Vyššie = lenivejšie.")]
    public float followSmoothTime = 0.25f;

    [Tooltip("Čas dobehnutia späť na poličku.")]
    public float shelfSmoothTime = 0.5f;

    [Tooltip("Čas dobehnutia, kým hráč ikonku drží. Nízke = lepie sa na ruku.")]
    public float heldSmoothTime = 0.06f;

    [Tooltip("Posuny menšie ako toto sa ignorujú, aby ikonka nešumela.")]
    public float deadZone = 0.002f;

    // ---- sieť -------------------------------------------------------------
    [UdonSynced] private Vector3 _syncedOffset;
    [UdonSynced] private bool _syncedDocked;

    // ---- stav -------------------------------------------------------------
    private Vector3 _offset;
    private VRCPlayerApi _target;
    private bool _localIsTarget;
    private bool _wasHeld;
    private bool _handleActive;
    private Vector3 _vel;
    private Quaternion _fixedRotation;
    private Vector3 _fallbackShelfPosition;
    private VRCPlayerApi[] _playerBuffer = new VRCPlayerApi[1];
    private bool _offsetIsCustom;      // true az ked si hrac ikonku sam potiahol
    private bool _isDocked;            // true = ciel ju odlozil na shelfAnchor
    private float _debugTimer;

    void Start()
    {
        _offset = defaultHeadOffset;
        _syncedOffset = defaultHeadOffset;
        _isDocked = false;
        _syncedDocked = false;

        if (follower == null) follower = transform;
        _fallbackShelfPosition = follower.position;
        _fixedRotation = shelfAnchor != null ? shelfAnchor.rotation : follower.rotation;
        follower.rotation = _fixedRotation;

        VRCPlayerApi local = Networking.LocalPlayer;
        _localIsTarget = local != null && (debugTreatLocalPlayerAsTarget || IsTargetName(local.displayName));

        RescanPlayers();
        SetHandleActive(false);

        if (_target == null)
            follower.position = ShelfPosition();
    }

    public override void OnPlayerJoined(VRCPlayerApi player) { RescanPlayers(); }
    public override void OnPlayerLeft(VRCPlayerApi player) { RescanPlayers(); }

    public override void OnDeserialization()
    {
        _offset = _syncedOffset;
        _isDocked = _syncedDocked;
        _offsetIsCustom = true;   // owner uz offset nastavil, default sa nepouziva
        _vel = Vector3.zero;
    }

    /// <summary>
    /// PostLateUpdate beží až potom, čo VRChat dopočíta pozície hráčov a IK pre tento
    /// snímok. V LateUpdate by ikonka čítala pozu o snímok staršiu a pri rýchlom pohybe
    /// (a najmä pri space drag) by za hlavou viditeľne zaostávala.
    /// </summary>
    public override void PostLateUpdate()
    {
        if (follower == null) return;

        bool hasTarget = _target != null && _target.IsValid();
        if (!hasTarget)
        {
            // Nikto - vraciame sa na poličku.
            SetHandleActive(false);
            MoveTowards(ShelfPosition(), shelfSmoothTime);
            follower.rotation = _fixedRotation;
            return;
        }

        Vector3 headPos = GetHeadPosition(_target);

        // Kým si ikonku nikto neposunul, drž východzí odstup prispôsobený veľkosti avatara.
        if (!_offsetIsCustom) _offset = DefaultOffsetFor(_target);

        // Úchyt existuje len na klientovi cieľového hráča.
        SetHandleActive(_localIsTarget);

        bool held = _localIsTarget && pickup != null && pickup.IsHeld;
        if (held)
        {
            // Chytenie odlepí ikonku z poličky. Sieťový stav publikujeme až pri
            // pustení, rovnako ako offset, aby sme neposielali zmenu každý frame.
            if (!_wasHeld && _isDocked)
            {
                _isDocked = false;
                _vel = Vector3.zero;
            }

            // Počas držania vedie úchyt - offset čítame z neho.
            Vector3 raw = grabHandle.transform.position - headPos;
            // rucny clamp - menej zavislosti na Udon expose zozname ako ClampMagnitude
            float len = raw.magnitude;
            if (len > maxOffsetDistance && len > 0.0001f)
                raw = raw * (maxOffsetDistance / len);
            _offset = raw;
            _offsetIsCustom = true;
            MoveTowards(headPos + _offset - RotatedHandleOffset(), heldSmoothTime);
        }
        else
        {
            // Pri pustení najprv rozhodni, či ikonku zacvaknúť na poličku.
            if (_wasHeld)
            {
                _isDocked = ShouldSnapToShelf();
                if (_isDocked)
                {
                    follower.position = ShelfPosition();
                    _vel = Vector3.zero;
                }
                PublishState();
            }

            if (_isDocked)
                MoveTowards(ShelfPosition(), shelfSmoothTime);
            else
                MoveTowards(headPos + _offset, followSmoothTime);

            if (_handleActive)
                grabHandle.transform.position = follower.position + RotatedHandleOffset();
        }
        _wasHeld = held;

        follower.rotation = _fixedRotation;
    }

    // -----------------------------------------------------------------------

    private void MoveTowards(Vector3 targetPos, float smoothTime)
    {
        if ((follower.position - targetPos).sqrMagnitude < deadZone * deadZone)
        {
            _vel = Vector3.zero;
            return;
        }
        // Lokálna kópia kvôli ref - UdonSharp má s ref na pole rád problémy.
        Vector3 vel = _vel;
        follower.position = Vector3.SmoothDamp(follower.position, targetPos, ref vel, smoothTime);
        _vel = vel;
    }

    private Vector3 RotatedHandleOffset()
    {
        return _fixedRotation * handleLocalOffset;
    }

    /// <summary>
    /// Kde je hlava cieľového hráča TAK, AKO JU VIDÍ TENTO KLIENT.
    ///
    /// Kľúčové je poradie zdrojov. Kosť hlavy avatara je jediné miesto, ktoré vždy sedí
    /// s tým, čo sa na obrazovke naozaj kreslí - vrátane prípadu, keď si hráč nadvihne
    /// playspace (OVRAS space drag). Vtedy kapsula hráča ostáva na zemi a tracking dáta
    /// pre vzdialených hráčov idú od nej, takže ikonka skončila v tele.
    /// </summary>
    private Vector3 GetHeadPosition(VRCPlayerApi player)
    {
        Vector3 capsule = player.GetPosition();
        float eye = player.GetAvatarEyeHeightAsMeters();
        if (eye < 0.05f) eye = 1.6f;

        Vector3 bone = player.GetBonePosition(HumanBodyBones.Head);
        VRCPlayerApi.TrackingData avatarRoot = player.GetTrackingData(VRCPlayerApi.TrackingDataType.AvatarRoot);
        VRCPlayerApi.TrackingData headTrack = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

        if (debugHeadSources)
        {
            _debugTimer += Time.deltaTime;
            if (_debugTimer >= 1f)
            {
                _debugTimer = 0f;
                Debug.Log("[YukineIcon] capsule=" + capsule.ToString("F3") +
                          " bone=" + bone.ToString("F3") +
                          " avatarRoot=" + avatarRoot.position.ToString("F3") +
                          " headTrack=" + headTrack.position.ToString("F3") +
                          " eyeHeight=" + eye.ToString("F3") +
                          " local=" + _localIsTarget.ToString());
            }
        }

        if (headSource == 1) return IsUsablePos(bone) ? bone : headTrack.position;
        if (headSource == 2) return headTrack.position;
        if (headSource == 3) return avatarRoot.position + Vector3.up * eye;

        // Auto
        if (IsUsablePos(bone)) return bone;
        if (IsUsablePos(avatarRoot.position)) return avatarRoot.position + Vector3.up * eye;
        if (IsUsablePos(headTrack.position)) return headTrack.position;
        return capsule + Vector3.up * eye;
    }

    /// <summary>VRChat vracia presne Vector3.zero, keď kosť/tracking nie je k dispozícii.</summary>
    private bool IsUsablePos(Vector3 p)
    {
        return p.sqrMagnitude > 0.0001f;
    }

    /// <summary>Východzí odstup prispôsobený veľkosti avatara (default je ladený na ~1.6 m postavu).</summary>
    private Vector3 DefaultOffsetFor(VRCPlayerApi player)
    {
        if (!scaleDefaultOffsetWithAvatar) return defaultHeadOffset;
        float eye = player.GetAvatarEyeHeightAsMeters();
        if (eye < 0.05f) return defaultHeadOffset;
        return defaultHeadOffset * (eye / 1.6f);
    }

    private bool ShouldSnapToShelf()
    {
        if (grabHandle == null) return false;

        // Follower počas držania dobieha ruku cez SmoothDamp, preto testujeme
        // priamo cieľovú polohu odvodenú z úchytu, nie mierne oneskorený follower.
        Vector3 releasedFollowerPosition = grabHandle.transform.position - RotatedHandleOffset();
        float snap = shelfSnapDistance;
        if (snap < 0f) snap = 0f;
        return (releasedFollowerPosition - ShelfPosition()).sqrMagnitude <= snap * snap;
    }

    private Vector3 ShelfPosition()
    {
        return shelfAnchor != null ? shelfAnchor.position : _fallbackShelfPosition;
    }

    private void PublishState()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;
        if (!Networking.IsOwner(local, gameObject))
            Networking.SetOwner(local, gameObject);
        _syncedOffset = _offset;
        _syncedDocked = _isDocked;
        RequestSerialization();
    }

    private void SetHandleActive(bool active)
    {
        if (grabHandle == null) return;
        if (_handleActive == active) return;
        _handleActive = active;
        grabHandle.SetActive(active);
    }

    private void RescanPlayers()
    {
        _target = null;

        int count = VRCPlayerApi.GetPlayerCount();
        if (count <= 0) return;
        if (_playerBuffer == null || _playerBuffer.Length < count)
            _playerBuffer = new VRCPlayerApi[count];

        VRCPlayerApi[] players = VRCPlayerApi.GetPlayers(_playerBuffer);
        for (int i = 0; i < players.Length; i++)
        {
            VRCPlayerApi p = players[i];
            if (p == null || !p.IsValid()) continue;

            if (debugTreatLocalPlayerAsTarget && p.isLocal) { _target = p; break; }
            if (IsTargetName(p.displayName)) { _target = p; break; }
        }

        VRCPlayerApi local = Networking.LocalPlayer;
        _localIsTarget = local != null && local.IsValid() && _target != null && _target.playerId == local.playerId;
    }

    private bool IsTargetName(string name)
    {
        if (string.IsNullOrEmpty(name) || targetDisplayNames == null) return false;
        for (int i = 0; i < targetDisplayNames.Length; i++)
        {
            string wanted = targetDisplayNames[i];
            if (!string.IsNullOrEmpty(wanted) && name == wanted) return true;
        }
        return false;
    }
}
