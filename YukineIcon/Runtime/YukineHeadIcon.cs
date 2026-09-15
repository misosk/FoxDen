using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Animovaná ikonka, ktorá stojí na poličke a vznáša sa nad hlavou konkrétneho hráča,
/// keď je v inštancii. Cieľový hráč si ju vie chytiť za neviditeľný pickup v bruchu
/// a posunúť relatívne k svojej hlave; offset sa synchronizuje ostatným.
///
/// Pozícia hlavy sa berie z tracking dát (replikované), takže si cieľ počíta každý
/// klient sám a po sieti ide len offset. Rotácia hlavy sa zámerne nepoužíva.
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

    [Tooltip("Miesto na poličke, kde ikonka stojí, keď cieľový hráč nie je v inštancii.")]
    public Transform shelfAnchor;

    [Tooltip("Capsule collider + VRC_Pickup. Aktívny len na klientovi cieľového hráča.")]
    public GameObject grabHandle;

    [Tooltip("VRC_Pickup na grabHandle.")]
    public VRC_Pickup pickup;

    [Header("Pozicovanie")]
    [Tooltip("Východiskový posun voči hlave, kým si ho hráč neposunie sám.")]
    public Vector3 defaultHeadOffset = new Vector3(0f, 0.45f, 0f);

    [Tooltip("Ako ďaleko od hlavy sa dá ikonka odtiahnuť.")]
    public float maxOffsetDistance = 1.5f;

    [Tooltip("Kde na ikonke sedí úchyt (v lokálnom priestore followera) - cca brucho.")]
    public Vector3 handleLocalOffset = new Vector3(0f, 0.15f, 0f);

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

    // ---- stav -------------------------------------------------------------
    private Vector3 _offset;
    private VRCPlayerApi _target;
    private bool _localIsTarget;
    private bool _wasHeld;
    private bool _handleActive;
    private Vector3 _vel;
    private Quaternion _fixedRotation;
    private VRCPlayerApi[] _playerBuffer = new VRCPlayerApi[1];

    void Start()
    {
        _offset = defaultHeadOffset;
        _syncedOffset = defaultHeadOffset;

        if (follower == null) follower = transform;
        _fixedRotation = shelfAnchor != null ? shelfAnchor.rotation : follower.rotation;
        follower.rotation = _fixedRotation;

        VRCPlayerApi local = Networking.LocalPlayer;
        _localIsTarget = local != null && (debugTreatLocalPlayerAsTarget || IsTargetName(local.displayName));

        RescanPlayers();
        SetHandleActive(false);

        if (shelfAnchor != null && _target == null)
            follower.position = shelfAnchor.position;
    }

    public override void OnPlayerJoined(VRCPlayerApi player) { RescanPlayers(); }
    public override void OnPlayerLeft(VRCPlayerApi player) { RescanPlayers(); }

    public override void OnDeserialization()
    {
        _offset = _syncedOffset;
    }

    void LateUpdate()
    {
        if (follower == null) return;

        bool hasTarget = _target != null && _target.IsValid();
        if (!hasTarget)
        {
            // Nikto - vraciame sa na poličku.
            SetHandleActive(false);
            if (shelfAnchor != null)
                MoveTowards(shelfAnchor.position, shelfSmoothTime);
            follower.rotation = _fixedRotation;
            return;
        }

        Vector3 headPos = GetHeadPosition(_target);

        // Úchyt existuje len na klientovi cieľového hráča.
        SetHandleActive(_localIsTarget);

        bool held = _localIsTarget && pickup != null && pickup.IsHeld;
        if (held)
        {
            // Počas držania vedie úchyt - offset čítame z neho.
            Vector3 raw = grabHandle.transform.position - headPos;
            // rucny clamp - menej zavislosti na Udon expose zozname ako ClampMagnitude
            float len = raw.magnitude;
            if (len > maxOffsetDistance && len > 0.0001f)
                raw = raw * (maxOffsetDistance / len);
            _offset = raw;
            MoveTowards(headPos + _offset - RotatedHandleOffset(), heldSmoothTime);
        }
        else
        {
            MoveTowards(headPos + _offset, followSmoothTime);
            if (_handleActive)
                grabHandle.transform.position = follower.position + RotatedHandleOffset();
        }

        if (_wasHeld && !held) PublishOffset();
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

    private Vector3 GetHeadPosition(VRCPlayerApi player)
    {
        VRCPlayerApi.TrackingData head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        return head.position;
    }

    private void PublishOffset()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (local == null) return;
        if (!Networking.IsOwner(local, gameObject))
            Networking.SetOwner(local, gameObject);
        _syncedOffset = _offset;
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
