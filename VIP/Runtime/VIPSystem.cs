
using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;


[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class VIPSystem : UdonSharpBehaviour
{
    [Header("Member Status Settings")]
    public TextMeshProUGUI displayMemberStatus;

    [Header("Guest Settings")]
    public bool usingGuestDisplayName;
    public TextMeshProUGUI displayGuestNameUI;

    [Header("VIP Settings")]
    public TextAsset vipList;
    public bool usingDisplayName;
    private string vipname;
    public bool usingVIPJoinSound;
    public AudioSource vipJoinSound;
    public TextMeshProUGUI displayNameUI;

    [Header("Enabled Objects for VIP")]
    public GameObject[] vipEnabledObjects;

    [Header("Disabled Objects for VIP")]
    public GameObject[] vipDisabledObjects;

    [Header("Staff Settings")]
    public bool usingStaff;
    public bool usingStaffJoinSound;
    public AudioSource staffJoinSound;
    public TextAsset StaffList;
    public bool usingStaffDisplayName;
    public TextMeshProUGUI StaffDisplayNameUI;

    [Header("Enabled Objects for Staff")]
    public GameObject[] StaffEnabledObjects;

    [Header("Disabled Objects for Staff")]
    public GameObject[] StaffDisabledObjects;

    [Header("VIP Spawn")]
    [Tooltip("VIP and Staff players are teleported here after joining and after every respawn. Leave empty to disable.")]
    public Transform vipSpawnPoint;
    [Tooltip("Seconds to wait after joining before teleporting, so VRChat's own spawn placement doesn't override it.")]
    public float vipSpawnDelay = 1f;

    [Header("Remote Sync (GitHub Pages)")]
    public bool useRemoteUrls = true;
    public VRCUrl remoteVipUrl = new VRCUrl("https://misosk.github.io/FoxDen/files/vip.txt");
    public VRCUrl remoteStaffUrl = new VRCUrl("https://misosk.github.io/FoxDen/files/staff.txt");
    public float retryDelay = 10f;

    private string[] vipNames;
    private string[] StaffNames;

    // Track which URLs have been loaded
    private bool _vipUrlLoaded = false;
    private bool _staffUrlLoaded = false;

    // VIP spawn state (local player only)
    private bool _localIsVip = false;
    private bool _localJoined = false;
    private bool _vipSpawnScheduled = false;

    private void Start()
    {
        // Load offline fallback lists from TextAssets
        if (Utilities.IsValid(vipList))
            vipNames = vipList.text.Split('\n');
        else
            vipNames = new string[0];

        if (Utilities.IsValid(StaffList))
            StaffNames = StaffList.text.Split('\n');
        else
            StaffNames = new string[0];

        // Evaluate local player status from offline lists first
        EvaluatePlayer();

        // Begin async fetch from GitHub Pages
        if (useRemoteUrls)
        {
            if (remoteVipUrl != null && !string.IsNullOrEmpty(remoteVipUrl.Get()))
                VRCStringDownloader.LoadUrl(remoteVipUrl, (IUdonEventReceiver)(Component)this);

            if (remoteStaffUrl != null && !string.IsNullOrEmpty(remoteStaffUrl.Get()))
                VRCStringDownloader.LoadUrl(remoteStaffUrl, (IUdonEventReceiver)(Component)this);
        }
    }

    // Called when a string URL download finishes successfully
    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        string url = result.Url.Get();
        string text = result.Result;
        string[] lines = SplitLines(text);

        if (url == remoteVipUrl.Get())
        {
            vipNames = lines;
            _vipUrlLoaded = true;
            Debug.Log("[VIPSystem] Remote VIP list loaded. Count: " + lines.Length);
        }
        else if (url == remoteStaffUrl.Get())
        {
            StaffNames = lines;
            _staffUrlLoaded = true;
            Debug.Log("[VIPSystem] Remote Staff list loaded. Count: " + lines.Length);
        }

        // Re-evaluate local player status with fresh lists
        EvaluatePlayer();
    }

    // Called when a string URL download fails
    public override void OnStringLoadError(IVRCStringDownload result)
    {
        string url = result.Url.Get();
        Debug.LogWarning("[VIPSystem] Failed to load remote URL: " + url
            + " | Error: " + result.Error
            + " | Code: " + result.ErrorCode
            + " — Keeping offline fallback list.");
    }

    // ---------------------------------------------------------------------------
    // Core evaluation: called on Start and after each remote download completes
    // ---------------------------------------------------------------------------
    private void EvaluatePlayer()
    {
        VRCPlayerApi player = Networking.LocalPlayer;
        if (!Utilities.IsValid(player)) return;

        string displayName = player.displayName;

        // ClientSim / local-test shortcut – give this player full access
        if (IsLocalTestPlayer(displayName))
        {
            // SetLocalVip first: an Udon exception in the UI updaters below (e.g. an unassigned TMP field)
            // aborts the rest of this method and would otherwise silently skip the VIP spawn.
            SetLocalVip(true);
            UpdateStateNitro(displayName);
            UpdateStateVIP(displayName);
            return;
        }

        bool isVIP   = ArrayContainsString(vipNames, displayName);
        bool isStaff = ArrayContainsString(StaffNames, displayName);

        if (isStaff && usingStaff)
        {
            SetLocalVip(true);
            UpdateStateNitro(displayName);
        }
        else if (isVIP)
        {
            SetLocalVip(true);
            UpdateStateVIP(displayName);
        }
        else
        {
            UpdateStateGuest(displayName);
            SetLocalVip(false);
        }
    }

    // ---------------------------------------------------------------------------
    // OnPlayerJoined – play join sound for other players entering the instance
    // ---------------------------------------------------------------------------
    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (Utilities.IsValid(player) && player.isLocal)
        {
            _localJoined = true;
            TryScheduleVipSpawn();
        }

        string displayName = player.displayName;

        bool isVIP   = ArrayContainsString(vipNames, displayName);
        bool isStaff = ArrayContainsString(StaffNames, displayName);

        if (isStaff && usingStaff)
        {
            if (usingStaffJoinSound)
                staffJoinSound.Play();
        }
        else if (isVIP)
        {
            if (usingVIPJoinSound)
                vipJoinSound.Play();
        }
    }

    // ---------------------------------------------------------------------------
    // VIP spawn – VIP / Staff spawn at vipSpawnPoint instead of the world spawn
    // ---------------------------------------------------------------------------

    private void SetLocalVip(bool isVip)
    {
        _localIsVip = isVip;
        TryScheduleVipSpawn();
    }

    // Teleports once per join, as soon as the local player has joined AND is known to be VIP/Staff.
    // The VIP check can finish before or after the join (offline list vs. remote list), so both paths call this.
    private void TryScheduleVipSpawn()
    {
        if (_vipSpawnScheduled || !_localIsVip || !_localJoined) return;
        if (!Utilities.IsValid(vipSpawnPoint)) return;

        _vipSpawnScheduled = true;
        SendCustomEventDelayedSeconds(nameof(_TeleportToVipSpawn), vipSpawnDelay);
    }

    public void _TeleportToVipSpawn()
    {
        VRCPlayerApi player = Networking.LocalPlayer;
        if (!_localIsVip || !Utilities.IsValid(player) || !Utilities.IsValid(vipSpawnPoint)) return;

        player.TeleportTo(vipSpawnPoint.position, vipSpawnPoint.rotation);
    }

    // VRChat respawn (menu respawn, falling out of the world) would put a VIP back at the normal spawn.
    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        _TeleportToVipSpawn();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>Returns true for ClientSim local test players (e.g. "[1] Local Player").</summary>
    private bool IsLocalTestPlayer(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name == "[1] Local Player" || name == "[2] Local Player" || name.StartsWith("[");
    }

    /// <summary>Splits a downloaded string on both \r\n and \n.</summary>
    private string[] SplitLines(string text)
    {
        // Replace \r\n first so we don't get empty entries
        text = text.Replace("\r\n", "\n");
        return text.Split('\n');
    }

    private bool ArrayContainsString(string[] array, string target)
    {
        if (array == null) return false;
        foreach (string element in array)
        {
            if (element.Trim().ToLower() == target.Trim().ToLower())
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------------------
    // State updaters
    // ---------------------------------------------------------------------------

    public void UpdateStateVIP(string playerName)
    {
        // If player is also staff, show Staff colouring
        bool isStaff = ArrayContainsString(StaffNames, playerName);

        if (usingDisplayName)
        {
            vipname = playerName;
            if (!string.IsNullOrEmpty(vipname))
            {
                Debug.Log("[VIPSystem] VIP Name: " + vipname);
                displayNameUI.text = " " + vipname;
                if (isStaff)
                {
                    displayNameUI.color     = Color.magenta;
                    displayMemberStatus.text  = "Staff";
                    displayMemberStatus.color = Color.magenta;
                }
                else
                {
                    displayNameUI.color     = Color.cyan;
                    displayMemberStatus.text  = "VIP";
                    displayMemberStatus.color = Color.cyan;
                }
            }
            else
            {
                Debug.LogWarning("[VIPSystem] VIP Name is empty or null!");
            }
        }

        foreach (GameObject obj in vipEnabledObjects)
            if (Utilities.IsValid(obj)) obj.SetActive(true);

        foreach (GameObject obj in vipDisabledObjects)
            if (Utilities.IsValid(obj)) obj.SetActive(false);
    }

    public void UpdateStateNitro(string playerName)
    {
        if (usingStaffDisplayName && usingStaff && Utilities.IsValid(StaffDisplayNameUI))
        {
            StaffDisplayNameUI.color     = Color.magenta;
            StaffDisplayNameUI.text      = playerName;
            displayMemberStatus.text     = "Staff";
            displayMemberStatus.color    = Color.magenta;
        }

        foreach (GameObject obj in StaffEnabledObjects)
            if (Utilities.IsValid(obj)) obj.SetActive(true);

        foreach (GameObject obj in StaffDisabledObjects)
            if (Utilities.IsValid(obj)) obj.SetActive(false);

        foreach (GameObject obj in vipEnabledObjects)
            if (Utilities.IsValid(obj)) obj.SetActive(true);

        foreach (GameObject obj in vipDisabledObjects)
            if (Utilities.IsValid(obj)) obj.SetActive(false);
    }

    public void UpdateStateGuest(string playerName)
    {
        if (usingGuestDisplayName)
        {
            displayGuestNameUI.text  = " " + playerName;
            displayGuestNameUI.color = Color.white;
            displayMemberStatus.text  = "Guest";
            displayMemberStatus.color = Color.white;
        }
    }

    // ---------------------------------------------------------------------------
    // Runtime list manipulation (kept for API compatibility)
    // ---------------------------------------------------------------------------

    public void AddVIP(string playerName)
    {
        if (!ArrayContainsString(vipNames, playerName))
        {
            string[] newVipNames = new string[vipNames.Length + 1];
            vipNames.CopyTo(newVipNames, 0);
            newVipNames[newVipNames.Length - 1] = playerName;
            vipNames = newVipNames;
            UpdateStateVIP(playerName);
        }
    }

    public void RemoveVIP(string playerName)
    {
        int index = Array.IndexOf(vipNames, playerName);
        if (index >= 0)
        {
            string[] newVipNames = new string[vipNames.Length - 1];
            Array.Copy(vipNames, 0, newVipNames, 0, index);
            Array.Copy(vipNames, index + 1, newVipNames, index, vipNames.Length - index - 1);
            vipNames = newVipNames;
        }
    }

    public void AddStaff(string playerName)
    {
        if (!ArrayContainsString(StaffNames, playerName))
        {
            string[] newStaffNames = new string[StaffNames.Length + 1];
            StaffNames.CopyTo(newStaffNames, 0);
            newStaffNames[newStaffNames.Length - 1] = playerName;
            StaffNames = newStaffNames;
            UpdateStateNitro(playerName);
        }
    }

    public void RemoveNitro(string playerName)
    {
        int index = Array.IndexOf(StaffNames, playerName);
        if (index >= 0)
        {
            string[] newStaffNames = new string[StaffNames.Length - 1];
            Array.Copy(StaffNames, 0, newStaffNames, 0, index);
            Array.Copy(StaffNames, index + 1, newStaffNames, index, StaffNames.Length - index - 1);
            StaffNames = newStaffNames;
        }
    }
}
