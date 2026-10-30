using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// Networked 4-player riichi mahjong.
/// Owner-authoritative: every player sends actions to the instance owner with NetAction; only the owner changes
/// the synced state, then serializes it and every client rebuilds the table visuals.
/// Seats run counter-clockwise 0..3; seat winds rotate with the dealer. Tile ids and meld kinds: see MahjongRules.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class MahjongGame : UdonSharpBehaviour
{
    [Header("Wired by Tools > Mahjong > Build Table")]
    public MahjongRules rules;
    public TMP_Text statusText;
    public Transform statusAnchor;
    public TMP_Text[] seatLabels;
    public MahjongTile[] handTiles;     // 4 seats x 14
    public MahjongTile[] discardTiles;  // 4 seats x 24
    public MahjongTile[] meldTiles;     // 4 seats x 4 melds x 4
    public MahjongTile[] doraTiles;     // 5 indicators
    public Transform announceAnchor;    // big last-discard tile + call announcements above the table
    public MahjongTile announceTile;
    public TMP_Text announceText;
    public TMP_Text announceCaption;
    public MahjongButton[] actionButtons; // 4 seats x ButtonSlots context buttons
    public Renderer[] tableRenderers;
    public Renderer[] buttonRenderers;
    public Renderer[] sitButtonRenderers;

    [Header("Materials (drop a material in to restyle everything that uses it)")]
    public Material tableMaterial;
    public Material tileMaterial;
    [Tooltip("Optional. If empty, tile backs are the tile material tinted green.")]
    public Material tileBackMaterial;
    [Tooltip("Tile face pictures: a 10 x 4 atlas laid out like MahjongTileAtlas.png.")]
    public Material tileFaceMaterial;
    public Material buttonMaterial;
    public Material sitButtonMaterial;
    [Tooltip("Show the number (or wind) in the corner of every tile picture.")]
    public bool showTileNumbers = true;

    [Header("Rules")]
    [Tooltip("1 = East round only (tonpuusen), 2 = East + South (hanchan).")]
    public int gameLength = 2;
    public int startingPoints = 25000;

    [Header("Timing")]
    [Tooltip("Seconds players get to Ron / Pon / Chi / Kan a discard before it auto-passes. 0 = wait forever.")]
    public float callWindowSeconds = 15f;
    [Tooltip("Pause after a discard nobody can call, before the next player draws.")]
    public float discardPauseSeconds = 1.2f;

    [Tooltip("Seconds before the next hand starts on its own after a win or draw. 0 = wait for Next Hand.")]
    public float nextHandSeconds = 12f;

    [Tooltip("How long call announcements (PON!, RIICHI! ...) stay up.")]
    public float announceSeconds = 2.5f;

    [Header("Bots")]
    [Tooltip("How long a bot 'thinks' before each move.")]
    public float botThinkSeconds = 1.0f;

    [Header("Testing")]
    [Tooltip("Lets the local player act for every seat and start without 4 players.")]
    public bool debugControlAllSeats = false;

    const int PhaseLobby = 0;
    const int PhaseTurn = 1;      // turn player discards (or tsumo / kan / riichi)
    const int PhaseCalls = 2;     // others may ron / pon / kan / chi the last discard
    const int PhaseHandOver = 3;
    const int PhaseGameOver = 4;
    const int PhasePause = 5;     // short pause after a discard before the next draw

    const int ActSit = 0;
    const int ActStartGame = 1;
    const int ActNext = 2;
    const int ActResetGame = 3;
    const int ActDiscardTile = 4;
    const int ActRiichiDiscard = 5;
    const int ActTsumoWin = 6;
    const int ActOwnKan = 7;
    const int ActPassCall = 8;
    const int ActRonWin = 9;
    const int ActPonCall = 10;
    const int ActChiCall = 11;
    const int ActKanCall = 12;
    const int ActBotToggle = 13;

    const int BotSeat = -2;       // seatPlayers value for a bot-controlled seat

    // Context buttons: each seat has ButtonSlots buttons that show only the choices available right now.
    public const int ButtonSlots = 7;
    const int BtnNone = 0;
    const int BtnTsumo = 1;
    const int BtnRiichi = 2;
    const int BtnOwnKan = 3;      // arg: tile type
    const int BtnRon = 4;
    const int BtnPon = 5;
    const int BtnChi = 6;         // arg: first tile type of the sequence
    const int BtnCallKan = 7;
    const int BtnPass = 8;
    const int BtnStart = 9;
    const int BtnNext = 10;
    const int BtnReset = 11;

    const int OptRon = 1;
    const int OptPon = 2;
    const int OptKan = 4;
    const int OptChi = 8;

    const int RespPending = 0;
    const int RespPass = 1;
    const int RespChi = 2;
    const int RespPon = 3;
    const int RespKan = 4;
    const int RespRon = 5;

    const int HandSlots = 14;
    const int DiscardSlots = 24;
    const int MeldSlots = 16;
    const int LiveWallEnd = 122;  // wall[122..135] is the dead wall: dora 122-126, ura 127-131, rinshan 132-135

    [UdonSynced] int[] seatPlayers = { -1, -1, -1, -1 };
    [UdonSynced] int phase = PhaseLobby;
    [UdonSynced] int dealer = 0;
    [UdonSynced] int roundWind = 0;
    [UdonSynced] int kyoku = 0;
    [UdonSynced] int honba = 0;
    [UdonSynced] int riichiSticks = 0;
    [UdonSynced] int[] scores = new int[4];
    [UdonSynced] int[] wall = new int[136];
    [UdonSynced] int wallPos = 0;
    [UdonSynced] int kanCount = 0;
    [UdonSynced] int[] hands = new int[56];
    [UdonSynced] int[] drawn = { -1, -1, -1, -1 };
    [UdonSynced] int[] discards = new int[96];
    [UdonSynced] int[] discardFlags = new int[96];   // 1 riichi declaration, 2 called away
    [UdonSynced] int[] discardCount = new int[4];
    [UdonSynced] int[] melds = new int[64];
    [UdonSynced] int[] meldKinds = new int[16];
    [UdonSynced] int[] meldCount = new int[4];
    [UdonSynced] int turn = 0;
    [UdonSynced] bool[] riichi = new bool[4];
    [UdonSynced] bool[] ippatsu = new bool[4];
    [UdonSynced] bool[] tempFuriten = new bool[4];
    [UdonSynced] int pendingRiichi = -1;
    [UdonSynced] bool canTsumo = false;
    [UdonSynced] bool afterKan = false;
    [UdonSynced] int lastDiscard = -1;
    [UdonSynced] int lastDiscarder = -1;
    [UdonSynced] int[] callOptions = new int[4];
    [UdonSynced] int[] callResponse = new int[4];
    [UdonSynced] int[] chiChoice = new int[4];
    [UdonSynced] bool renchan = false;
    [UdonSynced] bool drawHand = false;
    [UdonSynced] string lastEvent = "";
    [UdonSynced] int timerDeadline = 0;
    [UdonSynced] string announcement = "";
    [UdonSynced] int announceSerial = 0;   // server time (ms) when the call window / pause ends

    // ---- local only ----
    bool riichiArmed = false;
    int shownAnnounceSerial = 0;
    float announceHideTime = 0f;
    bool botStepQueued = false;   // owner only
    bool botRiichi = false;
    float nextCountdownRefresh = 0f;
    int selectedType = -1;
    // Opportunity highlights for the local player's turn, cached per draw (the riichi check is expensive in Udon).
    int hintKey = -1;
    bool hintTsumo = false;
    bool[] hintRiichi = new bool[34];
    bool[] hintKan = new bool[34];
    bool[] hintCall = new bool[34];
    int discardHintTint = 0;
    int[] displayIds = new int[56];
    int[] slotCode = new int[28];
    int[] slotArg = new int[28];
    int slotFill = 0;
    int[] tc = new int[34];
    bool[] waitMask = new bool[34];
    string[] windNames = { "East", "South", "West", "North" };
    string[] compassNames = { "Seat 1", "Seat 2", "Seat 3", "Seat 4" };

    void Start()
    {
        for (int i = 0; i < 56; i++) { hands[i] = -1; displayIds[i] = -1; }
        for (int i = 0; i < 64; i++) melds[i] = -1;
        for (int i = 0; i < 96; i++) discards[i] = -1;
        ApplyMaterials();
        Refresh();
    }

    /// Pushes the material slots onto every tile, button and table part.
    public void ApplyMaterials()
    {
        ApplyTileMaterials(handTiles);
        ApplyTileMaterials(discardTiles);
        ApplyTileMaterials(meldTiles);
        ApplyTileMaterials(doraTiles);
        if (announceTile != null)
        {
            announceTile.SetMaterials(tileMaterial, tileBackMaterial, tileFaceMaterial);
            announceTile.showNumber = showTileNumbers;
        }
        ApplyRendererMaterial(tableRenderers, tableMaterial);
        ApplyRendererMaterial(buttonRenderers, buttonMaterial);
        ApplyRendererMaterial(sitButtonRenderers, sitButtonMaterial);
    }

    void ApplyTileMaterials(MahjongTile[] tiles)
    {
        if (tiles == null) return;
        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i] == null) continue;
            tiles[i].SetMaterials(tileMaterial, tileBackMaterial, tileFaceMaterial);
            tiles[i].showNumber = showTileNumbers;
        }
    }

    void ApplyRendererMaterial(Renderer[] renderers, Material m)
    {
        if (renderers == null || m == null) return;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].sharedMaterial = m;
    }

    void Update()
    {
        bool counting = (phase == PhaseCalls && callWindowSeconds > 0f) || (phase == PhaseHandOver && nextHandSeconds > 0f);
        if (counting && statusText != null && Time.time >= nextCountdownRefresh)
        {
            nextCountdownRefresh = Time.time + 0.25f;
            statusText.text = BuildStatus();
        }
        if (announceText != null && announceHideTime > 0f && Time.time >= announceHideTime)
        {
            announceHideTime = 0f;
            announceText.text = "";
        }
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;
        Vector3 head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        FaceToward(statusAnchor, head);
        FaceToward(announceAnchor, head);
    }

    void FaceToward(Transform t, Vector3 head)
    {
        if (t == null) return;
        Vector3 dir = t.position - head;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(dir);
    }

    public override void OnDeserialization()
    {
        Refresh();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (!Networking.IsOwner(gameObject) || player == null) return;
        bool changed = false;
        for (int s = 0; s < 4; s++)
        {
            if (seatPlayers[s] != player.playerId) continue;
            seatPlayers[s] = IsRunning() ? BotSeat : -1;
            changed = true;
        }
        if (!changed) return;
        if (IsRunning()) lastEvent = "A player left. A bot took over their seat.";
        Commit();
    }

    // ================= local input =================

    public void ToggleSeat(int s)
    {
        Send(s, ActSit, 0);
    }

    public void ToggleBot(int s)
    {
        Send(s, ActBotToggle, 0);
    }

    public void ActStart() { SendAsSeated(ActStartGame); }
    public void ActNextHand() { SendAsSeated(ActNext); }
    public void ActReset() { SendAsSeated(ActResetGame); }

    void SendAsSeated(int action)
    {
        int s = LocalSeatOrAny();
        if (s >= 0) Send(s, action, 0);
    }

    public void ActRiichi()
    {
        int s = TurnSeat();
        if (s < 0) return;
        riichiArmed = !riichiArmed;
        Refresh();
        if (riichiArmed) Hint("Riichi armed: click the tile to discard.");
    }

    public void ActTsumo()
    {
        int s = TurnSeat();
        if (s >= 0) Send(s, ActTsumoWin, 0);
    }

    public void ActKan()
    {
        if (phase == PhaseCalls)
        {
            int c = CallSeat(OptKan);
            if (c >= 0) Send(c, ActKanCall, 0);
            return;
        }
        int s = TurnSeat();
        if (s < 0) return;
        int t = selectedType;
        if (t < 0 && drawn[s] >= 0) t = drawn[s] / 4;
        Send(s, ActOwnKan, t);
    }

    public void ActRon()
    {
        int s = CallSeat(OptRon);
        if (s >= 0) Send(s, ActRonWin, 0);
    }

    public void ActPon()
    {
        int s = CallSeat(OptPon);
        if (s >= 0) Send(s, ActPonCall, 0);
    }

    public void ActChi()
    {
        int s = CallSeat(OptChi);
        if (s < 0) return;
        int t = lastDiscard / 4;
        CountClosed(s, tc);
        int choice = -1;
        for (int st = t - 2; st <= t; st++)
        {
            if (!ChiPossible(st, t)) continue;
            if (choice < 0) choice = st;
            if (selectedType >= 0 && selectedType != t && selectedType >= st && selectedType <= st + 2) { choice = st; break; }
        }
        if (choice >= 0) Send(s, ActChiCall, choice);
    }

    public void ActPass()
    {
        int s = CallSeat(0);
        if (s >= 0) Send(s, ActPassCall, 0);
    }

    public void OnTileClicked(int s, int slot)
    {
        if (!ControlsSeat(s)) { Hint("Those aren't your tiles."); return; }
        int id = displayIds[s * HandSlots + slot];
        if (id < 0) return;

        if (phase == PhaseCalls)
        {
            // Picking a tile chooses which chi / kan to make when there's more than one.
            selectedType = selectedType == id / 4 ? -1 : id / 4;
            Refresh();
            return;
        }
        if (phase != PhaseTurn) return;
        if (s != turn) { Hint("It's " + SeatName(turn) + "'s turn."); return; }
        Send(s, riichiArmed ? ActRiichiDiscard : ActDiscardTile, id);
        riichiArmed = false;
        selectedType = -1;
    }

    void Send(int seat, int action, int arg)
    {
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetAction), seat, action, arg);
    }

    // ================= owner: action handling =================

    [NetworkCallable]
    public void NetAction(int seat, int action, int arg)
    {
        if (!Networking.IsOwner(gameObject) || seat < 0 || seat > 3) return;
        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (!Utilities.IsValid(caller)) return;
        int pid = caller.playerId;

        if (action == ActSit) { DoSit(seat, caller); return; }
        if (action == ActBotToggle) { DoToggleBot(seat); return; }

        bool seated = debugControlAllSeats || FindSeat(pid) >= 0;
        if (!seated) return;
        if (action == ActStartGame) { DoStartGame(); return; }
        if (action == ActNext) { DoNextHand(); return; }
        if (action == ActResetGame) { DoReset(); return; }

        if (!debugControlAllSeats && seatPlayers[seat] != pid) return;
        if (action == ActDiscardTile) DoDiscard(seat, arg, false);
        else if (action == ActRiichiDiscard) DoDiscard(seat, arg, true);
        else if (action == ActTsumoWin) DoTsumo(seat);
        else if (action == ActOwnKan) DoOwnKan(seat, arg);
        else if (action == ActPassCall) DoRespond(seat, RespPass, 0);
        else if (action == ActRonWin) DoRespond(seat, RespRon, 0);
        else if (action == ActPonCall) DoRespond(seat, RespPon, 0);
        else if (action == ActChiCall) DoRespond(seat, RespChi, arg);
        else if (action == ActKanCall) DoRespond(seat, RespKan, 0);
    }

    void DoSit(int s, VRCPlayerApi caller)
    {
        int pid = caller.playerId;
        bool running = IsRunning();
        if (seatPlayers[s] == pid)
        {
            // Standing up mid-game hands the seat to a bot so the game can continue.
            seatPlayers[s] = running ? BotSeat : -1;
            lastEvent = caller.displayName + " stood up." + (running ? " A bot took over." : "");
            Commit();
            return;
        }
        bool takingOverBot = seatPlayers[s] == BotSeat;
        if (seatPlayers[s] != -1 && !takingOverBot) return;
        if (running && !takingOverBot && !debugControlAllSeats) return;
        for (int i = 0; i < 4; i++) if (seatPlayers[i] == pid) seatPlayers[i] = running ? BotSeat : -1;
        seatPlayers[s] = pid;
        lastEvent = caller.displayName + (takingOverBot ? " took over from the bot." : " sat down.");
        Commit();
    }

    void DoToggleBot(int s)
    {
        if (IsRunning()) return; // mid-game, sit on a bot's seat to take it over instead
        if (seatPlayers[s] == -1) { seatPlayers[s] = BotSeat; lastEvent = SeatName(s) + " joined."; }
        else if (seatPlayers[s] == BotSeat) { seatPlayers[s] = -1; lastEvent = "Bot removed."; }
        else return;
        Commit();
    }

    void DoStartGame()
    {
        if (phase != PhaseLobby && phase != PhaseGameOver) return;
        if (!debugControlAllSeats && SeatedCount() < 4) { lastEvent = "Need 4 players to start."; Commit(); return; }
        for (int s = 0; s < 4; s++) scores[s] = startingPoints;
        dealer = Random.Range(0, 4);
        roundWind = 0;
        kyoku = 0;
        honba = 0;
        riichiSticks = 0;
        StartHand();
        lastEvent = "New game! " + SeatName(dealer) + " is the first dealer.";
        Commit();
    }

    void DoNextHand()
    {
        if (phase != PhaseHandOver) return;
        if (renchan) honba++;
        else
        {
            honba = drawHand ? honba + 1 : 0;
            dealer = (dealer + 1) % 4;
            kyoku++;
            if (kyoku >= 4) { kyoku = 0; roundWind++; }
        }
        StartHand();
        lastEvent = RoundName() + " begins.";
        Commit();
    }

    void DoReset()
    {
        phase = PhaseLobby;
        ClearTable();
        lastEvent = "Game reset.";
        Commit();
    }

    void StartHand()
    {
        for (int i = 0; i < 136; i++) wall[i] = i;
        for (int i = 135; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int t = wall[i]; wall[i] = wall[j]; wall[j] = t;
        }
        ClearTable();
        for (int s = 0; s < 4; s++)
            for (int k = 0; k < 13; k++)
                hands[s * HandSlots + k] = wall[wallPos++];
        turn = dealer;
        DrawTile(dealer, false);
        phase = PhaseTurn;
    }

    void ClearTable()
    {
        for (int i = 0; i < 56; i++) hands[i] = -1;
        for (int i = 0; i < 96; i++) { discards[i] = -1; discardFlags[i] = 0; }
        for (int i = 0; i < 64; i++) melds[i] = -1;
        for (int i = 0; i < 16; i++) meldKinds[i] = 0;
        for (int s = 0; s < 4; s++)
        {
            drawn[s] = -1;
            discardCount[s] = 0;
            meldCount[s] = 0;
            riichi[s] = false;
            ippatsu[s] = false;
            tempFuriten[s] = false;
            callOptions[s] = 0;
            callResponse[s] = RespPass;
        }
        wallPos = 0;
        kanCount = 0;
        pendingRiichi = -1;
        canTsumo = false;
        afterKan = false;
        lastDiscard = -1;
        lastDiscarder = -1;
        renchan = false;
        drawHand = false;
    }

    void DrawTile(int s, bool rinshan)
    {
        int id = rinshan ? wall[136 - kanCount] : wall[wallPos++];
        int slot = FirstEmptySlot(s);
        if (slot < 0) return;
        hands[s * HandSlots + slot] = id;
        drawn[s] = id;
        canTsumo = true;
        afterKan = rinshan;
    }

    void DoDiscard(int s, int id, bool declareRiichi)
    {
        if (phase != PhaseTurn || s != turn) return;
        int slot = SlotOf(s, id);
        if (slot < 0) return;
        if (riichi[s] && id != drawn[s]) { lastEvent = "After riichi you must discard the drawn tile."; Commit(); return; }

        if (declareRiichi)
        {
            if (riichi[s] || !IsClosed(s)) { lastEvent = "Riichi needs a closed hand."; Commit(); return; }
            if (scores[s] < 1000) { lastEvent = "Riichi needs 1000 points."; Commit(); return; }
            if (Remaining() < 4) { lastEvent = "Too few tiles left to riichi."; Commit(); return; }
            hands[s * HandSlots + slot] = -1;
            CountClosed(s, tc);
            bool tenpai = rules.IsTenpai(tc, meldCount[s]);
            hands[s * HandSlots + slot] = id;
            if (!tenpai) { lastEvent = "That discard doesn't leave you tenpai."; Commit(); return; }
        }

        hands[s * HandSlots + slot] = -1;
        drawn[s] = -1;
        canTsumo = false;
        afterKan = false;
        if (discardCount[s] < DiscardSlots)
        {
            int d = s * DiscardSlots + discardCount[s];
            discards[d] = id;
            discardFlags[d] = declareRiichi ? 1 : 0;
            discardCount[s]++;
        }
        ippatsu[s] = false;
        if (!riichi[s]) tempFuriten[s] = false;
        pendingRiichi = declareRiichi ? s : -1;
        lastDiscard = id;
        lastDiscarder = s;
        lastEvent = SeatName(s) + (declareRiichi ? " declared RIICHI!" : " discarded.");
        if (declareRiichi) Announce("<color=#ff5050>RIICHI!</color>", s);

        if (ComputeCallOptions())
        {
            phase = PhaseCalls;
            if (callWindowSeconds > 0f) StartTimer(callWindowSeconds);
        }
        else if (discardPauseSeconds > 0f)
        {
            phase = PhasePause;
            StartTimer(discardPauseSeconds);
        }
        else AdvanceAfterDiscard();
        Commit();
    }

    void StartTimer(float seconds)
    {
        timerDeadline = Networking.GetServerTimeInMilliseconds() + (int)(seconds * 1000f);
        SendCustomEventDelayedSeconds(nameof(_OnTimer), seconds + 0.05f);
    }

    /// Owner only. Ends the call window (auto-passing anyone who hasn't answered) or the post-discard pause.
    public void _OnTimer()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (phase != PhaseCalls && phase != PhasePause && phase != PhaseHandOver) return;
        int remaining = timerDeadline - Networking.GetServerTimeInMilliseconds();
        if (remaining > 100) return; // a newer timer is running

        if (phase == PhaseHandOver) { DoNextHand(); return; }
        if (phase == PhasePause) AdvanceAfterDiscard();
        else
        {
            for (int s = 0; s < 4; s++)
            {
                if (callResponse[s] != RespPending) continue;
                if ((callOptions[s] & OptRon) != 0) tempFuriten[s] = true;
                callResponse[s] = RespPass;
            }
            lastEvent = "Time's up - calls passed.";
            TryResolveCalls();
        }
        Commit();
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        // A new owner picks up any running timer and the bots.
        if (!Networking.IsOwner(gameObject)) return;
        botStepQueued = false;
        ScheduleBots();
        bool timed = phase == PhasePause || (phase == PhaseCalls && callWindowSeconds > 0f) || (phase == PhaseHandOver && nextHandSeconds > 0f);
        if (!timed) return;
        int remaining = timerDeadline - Networking.GetServerTimeInMilliseconds();
        SendCustomEventDelayedSeconds(nameof(_OnTimer), remaining > 0 ? remaining / 1000f + 0.05f : 0.1f);
    }

    bool ComputeCallOptions()
    {
        bool any = false;
        int t = lastDiscard / 4;
        bool canCall = Remaining() > 0;
        for (int s = 0; s < 4; s++)
        {
            int opts = 0;
            if (s != lastDiscarder)
            {
                CountClosed(s, tc);
                if (rules.IsWait(tc, meldCount[s], t) && !IsFuriten(s) && EvaluateRon(s)) opts |= OptRon;
                if (canCall && !riichi[s])
                {
                    if (tc[t] >= 2) opts |= OptPon;
                    if (tc[t] >= 3 && kanCount < 4) opts |= OptKan;
                    if (s == (lastDiscarder + 1) % 4 && t < 27)
                        for (int st = t - 2; st <= t; st++) if (ChiPossible(st, t)) opts |= OptChi;
                }
            }
            callOptions[s] = opts;
            callResponse[s] = opts == 0 ? RespPass : RespPending;
            chiChoice[s] = -1;
            if (opts != 0) any = true;
        }
        return any;
    }

    void AdvanceAfterDiscard()
    {
        EstablishRiichi();
        if (Remaining() <= 0) { ExhaustiveDraw(); return; }
        turn = (lastDiscarder + 1) % 4;
        DrawTile(turn, false);
        phase = PhaseTurn;
    }

    void EstablishRiichi()
    {
        if (pendingRiichi < 0) return;
        int s = pendingRiichi;
        riichi[s] = true;
        ippatsu[s] = true;
        scores[s] -= 1000;
        riichiSticks++;
        pendingRiichi = -1;
    }

    void DoRespond(int s, int resp, int arg)
    {
        if (phase != PhaseCalls || callResponse[s] != RespPending) return;
        int opts = callOptions[s];
        if (resp == RespRon && (opts & OptRon) == 0) return;
        if (resp == RespPon && (opts & OptPon) == 0) return;
        if (resp == RespKan && (opts & OptKan) == 0) return;
        if (resp == RespChi)
        {
            if ((opts & OptChi) == 0) return;
            CountClosed(s, tc);
            if (!ChiPossible(arg, lastDiscard / 4)) return;
            chiChoice[s] = arg;
        }
        // Declining a ron makes you furiten until your next discard (permanently once in riichi).
        if (resp != RespRon && (opts & OptRon) != 0) tempFuriten[s] = true;
        callResponse[s] = resp;
        TryResolveCalls();
        Commit();
    }

    void TryResolveCalls()
    {
        int best = -1;
        int bestRank = 0;
        for (int i = 1; i < 4; i++)
        {
            int s = (lastDiscarder + i) % 4;
            int r = ResponseRank(callResponse[s]);
            if (r > bestRank) { bestRank = r; best = s; }
        }
        for (int i = 1; i < 4; i++)
        {
            int s = (lastDiscarder + i) % 4;
            if (callResponse[s] == RespPending && MaxOptionRank(callOptions[s]) >= (bestRank > 0 ? bestRank : 1)) return;
        }

        if (bestRank == 0) { AdvanceAfterDiscard(); return; }
        int resp = callResponse[best];
        if (resp == RespRon) { WinByRon(best); return; }

        EstablishRiichi();
        for (int s = 0; s < 4; s++) ippatsu[s] = false;
        int t = lastDiscard / 4;
        int m = meldCount[best];
        int mBase = best * MeldSlots + m * 4;
        melds[mBase] = lastDiscard;
        if (discardCount[lastDiscarder] > 0) discardFlags[lastDiscarder * DiscardSlots + discardCount[lastDiscarder] - 1] |= 2;
        string callName;
        if (resp == RespChi)
        {
            int st = chiChoice[best];
            int k = 1;
            for (int x = st; x <= st + 2; x++)
            {
                if (x == t) continue;
                melds[mBase + k] = TakeTile(best, x);
                k++;
            }
            meldKinds[best * 4 + m] = MahjongRules.MeldChi;
            callName = "Chi";
        }
        else if (resp == RespPon)
        {
            melds[mBase + 1] = TakeTile(best, t);
            melds[mBase + 2] = TakeTile(best, t);
            meldKinds[best * 4 + m] = MahjongRules.MeldPon;
            callName = "Pon";
        }
        else
        {
            melds[mBase + 1] = TakeTile(best, t);
            melds[mBase + 2] = TakeTile(best, t);
            melds[mBase + 3] = TakeTile(best, t);
            meldKinds[best * 4 + m] = MahjongRules.MeldOpenKan;
            callName = "Kan";
        }
        meldCount[best]++;
        turn = best;
        phase = PhaseTurn;
        drawn[best] = -1;
        canTsumo = false;
        afterKan = false;
        if (resp == RespKan)
        {
            kanCount++;
            DrawTile(best, true);
        }
        lastEvent = SeatName(best) + " called " + callName + "!";
        Announce("<color=#ffd040>" + callName.ToUpper() + "!</color>", best);
    }

    void DoOwnKan(int s, int t)
    {
        if (phase != PhaseTurn || s != turn || drawn[s] < 0) return;
        if (riichi[s]) { lastEvent = "Kan isn't allowed after riichi here."; Commit(); return; }
        if (kanCount >= 4 || Remaining() <= 0) { lastEvent = "No more kans allowed."; Commit(); return; }
        CountClosed(s, tc);

        // Pick the requested type, or any type that can be kanned.
        if (t < 0 || !(tc[t] == 4 || (tc[t] >= 1 && PonMeldOf(s, t) >= 0)))
        {
            t = -1;
            for (int x = 0; x < 34 && t < 0; x++) if (tc[x] == 4 || (tc[x] >= 1 && PonMeldOf(s, x) >= 0)) t = x;
        }
        if (t < 0) { lastEvent = "You have nothing to kan."; Commit(); return; }

        int m;
        if (tc[t] == 4)
        {
            m = meldCount[s];
            for (int k = 0; k < 4; k++) melds[s * MeldSlots + m * 4 + k] = TakeTile(s, t);
            meldKinds[s * 4 + m] = MahjongRules.MeldClosedKan;
            meldCount[s]++;
            lastEvent = SeatName(s) + " declared a closed Kan.";
            Announce("<color=#ffd040>KAN!</color>", s);
        }
        else
        {
            m = PonMeldOf(s, t);
            melds[s * MeldSlots + m * 4 + 3] = TakeTile(s, t);
            meldKinds[s * 4 + m] = MahjongRules.MeldAddedKan;
            lastEvent = SeatName(s) + " added to a Kan.";
            Announce("<color=#ffd040>KAN!</color>", s);
        }
        for (int i = 0; i < 4; i++) ippatsu[i] = false;
        kanCount++;
        drawn[s] = -1;
        DrawTile(s, true);
        Commit();
    }

    void DoTsumo(int s)
    {
        if (phase != PhaseTurn || s != turn || !canTsumo || drawn[s] < 0) return;
        CountClosed(s, tc);
        if (!rules.IsCompleteShape(tc, meldCount[s])) { lastEvent = "That hand isn't complete."; Commit(); return; }
        if (!Evaluate(s, -1, drawn[s], true)) { lastEvent = "No yaku - can't win with that hand."; Commit(); return; }

        int b = rules.resultBase;
        int total = 0;
        for (int o = 0; o < 4; o++)
        {
            if (o == s) continue;
            int pay = rules.RoundUp100(b * ((s == dealer || o == dealer) ? 2 : 1)) + 100 * honba;
            scores[o] -= pay;
            total += pay;
        }
        total += riichiSticks * 1000;
        scores[s] += total;
        riichiSticks = 0;
        lastEvent = SeatName(s) + " wins by TSUMO! " + ResultLine() + " (+" + total + ")";
        Announce("<color=#ff7755>TSUMO!</color>", s);
        EndHand(s == dealer, false);
        Commit();
    }

    void WinByRon(int s)
    {
        pendingRiichi = -1; // a riichi declaration tile that deals in doesn't count
        Evaluate(s, lastDiscard, lastDiscard, false);
        int pay = rules.RoundUp100(rules.resultBase * (s == dealer ? 6 : 4)) + 300 * honba;
        scores[lastDiscarder] -= pay;
        scores[s] += pay + riichiSticks * 1000;
        int total = pay + riichiSticks * 1000;
        riichiSticks = 0;
        lastEvent = SeatName(s) + " wins by RON from " + SeatName(lastDiscarder) + "! " + ResultLine() + " (+" + total + ")";
        Announce("<color=#ff7755>RON!</color>", s);
        EndHand(s == dealer, false);
    }

    void ExhaustiveDraw()
    {
        bool[] tenpai = new bool[4];
        int n = 0;
        for (int s = 0; s < 4; s++)
        {
            CountClosed(s, tc);
            tenpai[s] = riichi[s] || rules.IsTenpai(tc, meldCount[s]);
            if (tenpai[s]) n++;
        }
        string names = "";
        if (n > 0 && n < 4)
        {
            for (int s = 0; s < 4; s++)
            {
                if (tenpai[s]) { scores[s] += 3000 / n; names += (names.Length > 0 ? ", " : "") + SeatName(s); }
                else scores[s] -= 3000 / (4 - n);
            }
        }
        lastEvent = "Exhaustive draw. Tenpai: " + (n == 0 ? "nobody" : (n == 4 ? "everyone" : names)) + ".";
        Announce("<color=#a0c8ff>DRAW</color>", -1);
        EndHand(tenpai[dealer], true);
    }

    void EndHand(bool dealerKeeps, bool isDraw)
    {
        renchan = dealerKeeps;
        drawHand = isDraw;
        pendingRiichi = -1;
        bool bust = false;
        for (int s = 0; s < 4; s++) if (scores[s] < 0) bust = true;
        bool lastHand = roundWind >= gameLength - 1 && kyoku >= 3;
        if (bust || (lastHand && !dealerKeeps))
        {
            phase = PhaseGameOver;
            lastEvent += "\n<b>Game over!</b> " + Standings();
        }
        else
        {
            phase = PhaseHandOver;
            if (nextHandSeconds > 0f) StartTimer(nextHandSeconds);
        }
    }

    bool EvaluateRon(int s)
    {
        return Evaluate(s, lastDiscard, lastDiscard, false);
    }

    bool Evaluate(int s, int extraId, int winId, bool tsumo)
    {
        bool last = Remaining() <= 0 && !(tsumo && afterKan);
        return rules.EvaluateWin(hands, s * HandSlots, extraId, melds, meldKinds, s, meldCount[s],
            winId, tsumo, SeatWind(s), roundWind, riichi[s], ippatsu[s], last, tsumo && afterKan, wall, 1 + kanCount);
    }

    string ResultLine()
    {
        string limit = rules.LimitName(rules.resultBase, rules.resultYakuman);
        string size = rules.resultYakuman > 0 ? limit : rules.resultHan + " han " + rules.resultFu + " fu" + (limit.Length > 0 ? " " + limit : "");
        return rules.resultText + " = " + size;
    }

    // ================= helpers =================

    bool IsFuriten(int s)
    {
        if (tempFuriten[s]) return true;
        CountClosed(s, tc);
        for (int t = 0; t < 34; t++) waitMask[t] = rules.IsWait(tc, meldCount[s], t);
        for (int i = 0; i < discardCount[s]; i++)
        {
            int d = discards[s * DiscardSlots + i];
            if (d >= 0 && waitMask[d / 4]) return true;
        }
        return false;
    }

    /// Chi using sequence st..st+2 with discard type t, using tiles currently counted in tc.
    bool ChiPossible(int st, int t)
    {
        if (st < 0 || t >= 27 || st / 9 != t / 9 || st % 9 > 6) return false;
        for (int x = st; x <= st + 2; x++) if (x != t && tc[x] < 1) return false;
        return true;
    }

    int ResponseRank(int resp)
    {
        if (resp == RespRon) return 3;
        if (resp == RespPon || resp == RespKan) return 2;
        if (resp == RespChi) return 1;
        return 0;
    }

    int MaxOptionRank(int opts)
    {
        if ((opts & OptRon) != 0) return 3;
        if ((opts & (OptPon | OptKan)) != 0) return 2;
        if ((opts & OptChi) != 0) return 1;
        return 0;
    }

    int Remaining()
    {
        return LiveWallEnd - kanCount - wallPos;
    }

    int SeatWind(int s)
    {
        return (s - dealer + 4) % 4;
    }

    void CountClosed(int s, int[] c)
    {
        for (int i = 0; i < 34; i++) c[i] = 0;
        for (int k = 0; k < HandSlots; k++)
        {
            int id = hands[s * HandSlots + k];
            if (id >= 0) c[id / 4]++;
        }
    }

    bool IsClosed(int s)
    {
        for (int m = 0; m < meldCount[s]; m++) if (meldKinds[s * 4 + m] != MahjongRules.MeldClosedKan) return false;
        return true;
    }

    int PonMeldOf(int s, int t)
    {
        for (int m = 0; m < meldCount[s]; m++)
            if (meldKinds[s * 4 + m] == MahjongRules.MeldPon && melds[s * MeldSlots + m * 4] / 4 == t) return m;
        return -1;
    }

    /// Removes one tile of type t from the hand (keeping red fives for last) and returns its id.
    int TakeTile(int s, int t)
    {
        int pick = -1;
        for (int k = 0; k < HandSlots; k++)
        {
            int id = hands[s * HandSlots + k];
            if (id < 0 || id / 4 != t) continue;
            if (pick < 0 || rules.IsAka(hands[s * HandSlots + pick])) pick = k;
        }
        if (pick < 0) return -1;
        int taken = hands[s * HandSlots + pick];
        hands[s * HandSlots + pick] = -1;
        return taken;
    }

    int SlotOf(int s, int id)
    {
        for (int k = 0; k < HandSlots; k++) if (hands[s * HandSlots + k] == id) return k;
        return -1;
    }

    int FirstEmptySlot(int s)
    {
        for (int k = 0; k < HandSlots; k++) if (hands[s * HandSlots + k] < 0) return k;
        return -1;
    }

    int FindSeat(int pid)
    {
        for (int s = 0; s < 4; s++) if (seatPlayers[s] == pid) return s;
        return -1;
    }

    int SeatedCount()
    {
        int n = 0;
        for (int s = 0; s < 4; s++) if (seatPlayers[s] != -1) n++;
        return n;
    }

    bool IsRunning()
    {
        return phase != PhaseLobby && phase != PhaseGameOver;
    }

    bool IsBot(int s)
    {
        return seatPlayers[s] == BotSeat;
    }

    bool ControlsSeat(int s)
    {
        if (IsBot(s)) return false;
        if (debugControlAllSeats) return true;
        VRCPlayerApi local = Networking.LocalPlayer;
        return Utilities.IsValid(local) && seatPlayers[s] == local.playerId;
    }

    int LocalSeat()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        return Utilities.IsValid(local) ? FindSeat(local.playerId) : -1;
    }

    int LocalSeatOrAny()
    {
        int s = LocalSeat();
        if (s < 0 && !debugControlAllSeats) { Hint("Sit down first."); return -1; }
        return s < 0 ? 0 : s;
    }

    /// The seat whose turn it is, if the local player controls it.
    int TurnSeat()
    {
        if (phase != PhaseTurn) { Hint("It's not time for that."); return -1; }
        if (!ControlsSeat(turn)) { Hint("It's " + SeatName(turn) + "'s turn."); return -1; }
        return turn;
    }

    /// A seat the local player controls that is still deciding on the last discard (and has option opt, if non-zero).
    int CallSeat(int opt)
    {
        if (phase != PhaseCalls) { Hint("There's nothing to call right now."); return -1; }
        for (int i = 1; i < 4; i++)
        {
            int s = (lastDiscarder + i) % 4;
            if (callResponse[s] != RespPending || !ControlsSeat(s)) continue;
            if (opt == 0 || (callOptions[s] & opt) != 0) return s;
        }
        Hint("You can't do that on this discard.");
        return -1;
    }

    string SeatName(int s)
    {
        if (s < 0 || s > 3) return "?";
        if (IsBot(s)) return "Bot " + (s + 1);
        VRCPlayerApi p = seatPlayers[s] >= 0 ? VRCPlayerApi.GetPlayerById(seatPlayers[s]) : null;
        return Utilities.IsValid(p) ? p.displayName : compassNames[s];
    }

    string RoundName()
    {
        return windNames[roundWind % 4] + " " + (kyoku + 1);
    }

    string Standings()
    {
        bool[] done = new bool[4];
        string text = "";
        for (int place = 1; place <= 4; place++)
        {
            int best = -1;
            for (int s = 0; s < 4; s++) if (!done[s] && (best < 0 || scores[s] > scores[best])) best = s;
            done[best] = true;
            text += (place > 1 ? "  " : "") + place + ". " + SeatName(best) + " " + scores[best];
        }
        return text;
    }

    void Commit()
    {
        RequestSerialization();
        Refresh();
        ScheduleBots();
    }

    // ================= bots (run by the owner) =================
    // Discards maximise tile acceptance at the lowest shanten, keep dora / red fives / yakuhai pairs, and steer
    // open hands toward the yaku they're going for. Against riichi they push when close to tenpai and fold
    // (genbutsu, suji, visible honors) otherwise. Calls are only made when they speed up a hand that keeps a yaku.

    const int RouteClosed = 0;    // closed hand: riichi / menzen tsumo always give a yaku
    const int RouteFree = 1;      // open with a yakuhai triplet already: anything goes
    const int RouteTanyao = 2;    // open, all simples so far
    const int RouteHonitsu = 3;   // open, one suit + honors (RouteHonitsu + suit)
    const int RouteNone = -1;     // open with no clear yaku: just play fast

    int[] botUnseen = new int[34];
    int[] botShanten = new int[34];
    int[] botDanger = new int[34];

    void ScheduleBots()
    {
        if (botStepQueued || !Networking.IsOwner(gameObject) || BotToAct() < 0) return;
        botStepQueued = true;
        SendCustomEventDelayedSeconds(nameof(_BotStep), botThinkSeconds > 0f ? botThinkSeconds : 0.05f);
    }

    public void _BotStep()
    {
        botStepQueued = false;
        if (!Networking.IsOwner(gameObject)) return;
        int s = BotToAct();
        if (s < 0) return;
        if (phase == PhaseTurn) BotTurn(s);
        else BotCall(s);
    }

    int BotToAct()
    {
        if (phase == PhaseTurn) return IsBot(turn) ? turn : -1;
        if (phase == PhaseCalls)
        {
            for (int i = 1; i < 4; i++)
            {
                int s = (lastDiscarder + i) % 4;
                if (callResponse[s] == RespPending && IsBot(s)) return s;
            }
        }
        return -1;
    }

    void BotTurn(int s)
    {
        CountClosed(s, tc);
        if (canTsumo && drawn[s] >= 0 && rules.IsCompleteShape(tc, meldCount[s]) && Evaluate(s, -1, drawn[s], true))
        {
            DoTsumo(s);
            return;
        }
        if (riichi[s])
        {
            DoDiscard(s, drawn[s] >= 0 ? drawn[s] : BotTileOfType(s, BotFirstType(s)), false);
            return;
        }
        int t = BotChooseDiscard(s);
        int kan = BotKanChoice(s, t);
        if (kan >= 0) { DoOwnKan(s, kan); return; }
        DoDiscard(s, BotTileOfType(s, t), botRiichi);
    }

    /// Picks the tile type to discard and sets botRiichi. Leaves botShanten filled for BotKanChoice.
    int BotChooseDiscard(int s)
    {
        botRiichi = false;
        CountClosed(s, tc);
        BotComputeUnseen(s);
        int riichiThreats = BotComputeDanger(s);
        int route = BotRoute(s);
        int mc = meldCount[s];

        int minSh = 99;
        for (int x = 0; x < 34; x++)
        {
            botShanten[x] = 99;
            if (tc[x] == 0) continue;
            tc[x]--;
            botShanten[x] = rules.Shanten(tc, mc);
            tc[x]++;
            if (botShanten[x] < minSh) minSh = botShanten[x];
        }

        // Push when tenpai, or one away with plenty of wall left; otherwise fold against riichi.
        bool fold = riichiThreats > 0 && !(minSh == 0 || (minSh == 1 && Remaining() >= 40));

        int best = -1;
        int bestValue = 0;
        bool bestTenpaiOk = false;
        int bestWaits = 0;
        for (int x = 0; x < 34; x++)
        {
            if (tc[x] == 0) continue;
            int value = -1000 * (botShanten[x] - minSh);
            int waits = 0;
            bool tenpaiOk = false;
            if (botShanten[x] == minSh)
            {
                tc[x]--;
                waits = rules.Ukeire(tc, mc, botUnseen);
                if (botShanten[x] == 0) tenpaiOk = !BotFuritenAfter(s, x);
                tc[x]++;
                value += 8 * waits;
                if (botShanten[x] == 0 && !tenpaiOk) value -= 150;   // discarding into furiten
            }
            value -= 25 * BotDoraValue(s, x);
            if (IsYakuhaiFor(s, x) && tc[x] >= 2) value -= 30;
            if (route == RouteTanyao && rules.IsTermOrHonor(x)) value += 60;
            if (route >= RouteHonitsu && x < 27 && x / 9 != route - RouteHonitsu) value += 60;
            if (rules.IsTermOrHonor(x) && tc[x] == 1) value += 2;   // early on, isolated terminals / honors go first

            if (fold) value = -1000 * botDanger[x] + value / 50;
            else value -= 15 * botDanger[x];

            if (best < 0 || value > bestValue)
            {
                best = x;
                bestValue = value;
                bestTenpaiOk = tenpaiOk;
                bestWaits = waits;
            }
        }

        if (best >= 0 && botShanten[best] == 0 && route == RouteClosed && bestTenpaiOk && bestWaits > 0)
            botRiichi = scores[s] >= 1000 && Remaining() >= 4;
        return best;
    }

    /// A kan type to declare now (with intended discard type t already chosen), or -1.
    int BotKanChoice(int s, int t)
    {
        if (drawn[s] < 0 || kanCount >= 4 || Remaining() <= 0 || t < 0) return -1;
        for (int o = 0; o < 4; o++) if (o != s && riichi[o]) return -1;   // no extra dora while someone's in riichi
        CountClosed(s, tc);
        int current = botShanten[t];
        for (int x = 0; x < 34; x++)
        {
            if (tc[x] == 4)
            {
                tc[x] = 0;
                int after = rules.Shanten(tc, meldCount[s] + 1);
                tc[x] = 4;
                if (after <= current) return x;
            }
            else if (tc[x] >= 1 && PonMeldOf(s, x) >= 0 && botShanten[x] <= current) return x;
        }
        return -1;
    }

    /// Bots only call when it lowers shanten and the hand keeps a way to win (yakuhai, tanyao or honitsu).
    void BotCall(int s)
    {
        int opts = callOptions[s];
        if ((opts & OptRon) != 0) { DoRespond(s, RespRon, 0); return; }
        int t = lastDiscard / 4;
        bool threat = false;
        for (int o = 0; o < 4; o++) if (o != s && riichi[o]) threat = true;
        CountClosed(s, tc);
        int mc = meldCount[s];
        int before = rules.Shanten(tc, mc);
        if (threat && before >= 1) { DoRespond(s, RespPass, 0); return; }

        if ((opts & OptPon) != 0 && BotCallKeepsYaku(s, t, t))
        {
            tc[t] -= 2;
            int after = BotBestShantenAfterCall(mc + 1);
            tc[t] += 2;
            if (after < before || (IsYakuhaiFor(s, t) && after <= before)) { DoRespond(s, RespPon, 0); return; }
        }
        if ((opts & OptChi) != 0)
        {
            int bestSt = -1;
            int bestAfter = before;
            for (int st = t - 2; st <= t; st++)
            {
                if (!ChiPossible(st, t) || !BotCallKeepsYaku(s, st, st + 2)) continue;
                for (int x = st; x <= st + 2; x++) if (x != t) tc[x]--;
                int after = BotBestShantenAfterCall(mc + 1);
                for (int x = st; x <= st + 2; x++) if (x != t) tc[x]++;
                if (after < bestAfter) { bestAfter = after; bestSt = st; }
            }
            if (bestSt >= 0) { DoRespond(s, RespChi, bestSt); return; }
        }
        DoRespond(s, RespPass, 0);
    }

    /// Best shanten after discarding one tile from tc (used right after a call takes tiles out).
    int BotBestShantenAfterCall(int meldsAfter)
    {
        int best = 99;
        for (int x = 0; x < 34; x++)
        {
            if (tc[x] == 0) continue;
            tc[x]--;
            int sh = rules.Shanten(tc, meldsAfter);
            tc[x]++;
            if (sh < best) best = sh;
        }
        return best;
    }

    /// Would a new open set covering tile types lo..hi (a pon has lo == hi) leave the hand a realistic yaku?
    /// tc holds the closed hand before the call.
    bool BotCallKeepsYaku(int s, int lo, int hi)
    {
        if (lo == hi && IsYakuhaiFor(s, lo)) return true;
        if (BotHasYakuhaiMeld(s)) return true;
        for (int y = 27; y < 34; y++) if (tc[y] >= 3 && IsYakuhaiFor(s, y)) return true;

        // Tanyao: the new set and every meld simple, at most two terminals / honors left to throw.
        bool simpleSet = !rules.IsTermOrHonor(lo) && !rules.IsTermOrHonor(hi);
        if (simpleSet && BotMeldsAllSimple(s))
        {
            int junk = 0;
            for (int x = 0; x < 34; x++) if (rules.IsTermOrHonor(x)) junk += tc[x];
            if (junk <= 2) return true;
        }

        // Honitsu: one suit plus honors, at most three off-suit tiles left to throw.
        int suit = lo < 27 ? lo / 9 : BotMainSuit();
        if (BotMeldsFitSuit(s, suit))
        {
            int off = 0;
            for (int x = 0; x < 27; x++) if (x / 9 != suit) off += tc[x];
            if (off <= 3) return true;
        }
        return false;
    }

    int BotRoute(int s)
    {
        if (IsClosed(s)) return RouteClosed;
        if (BotHasYakuhaiMeld(s)) return RouteFree;
        if (BotMeldsAllSimple(s)) return RouteTanyao;
        int suit = -1;
        for (int m = 0; m < meldCount[s]; m++)
        {
            int t = melds[s * MeldSlots + m * 4] / 4;
            if (t < 27) suit = t / 9;
        }
        if (suit < 0) suit = BotMainSuit();
        if (BotMeldsFitSuit(s, suit)) return RouteHonitsu + suit;
        return RouteNone;
    }

    bool BotMeldsAllSimple(int s)
    {
        for (int k = 0; k < meldCount[s] * 4; k++)
        {
            int id = melds[s * MeldSlots + k];
            if (id >= 0 && rules.IsTermOrHonor(id / 4)) return false;
        }
        return true;
    }

    bool BotMeldsFitSuit(int s, int suit)
    {
        for (int k = 0; k < meldCount[s] * 4; k++)
        {
            int id = melds[s * MeldSlots + k];
            if (id >= 0 && id / 4 < 27 && id / 36 != suit) return false;
        }
        return true;
    }

    /// The suit with the most tiles in tc.
    int BotMainSuit()
    {
        int best = 0;
        int bestCount = -1;
        for (int suit = 0; suit < 3; suit++)
        {
            int n = 0;
            for (int x = suit * 9; x < suit * 9 + 9; x++) n += tc[x];
            if (n > bestCount) { bestCount = n; best = suit; }
        }
        return best;
    }

    /// Fills botUnseen: copies of each type the bot can't see (not in its hand, any pond, any meld or the dora indicators).
    void BotComputeUnseen(int s)
    {
        for (int x = 0; x < 34; x++) botUnseen[x] = 4 - tc[x];
        for (int o = 0; o < 4; o++)
        {
            for (int i = 0; i < discardCount[o]; i++)
            {
                int k = o * DiscardSlots + i;
                if (discards[k] >= 0 && (discardFlags[k] & 2) == 0) botUnseen[discards[k] / 4]--;
            }
            for (int k = 0; k < meldCount[o] * 4; k++)
            {
                int id = melds[o * MeldSlots + k];
                if (id >= 0) botUnseen[id / 4]--;
            }
        }
        for (int d = 0; d < 1 + kanCount; d++) botUnseen[wall[122 + d] / 4]--;
        for (int x = 0; x < 34; x++) if (botUnseen[x] < 0) botUnseen[x] = 0;
    }

    /// Fills botDanger (riichi players count double, players with 3+ open melds count once) and returns
    /// the number of opponents in riichi.
    int BotComputeDanger(int s)
    {
        for (int x = 0; x < 34; x++) botDanger[x] = 0;
        int riichiCount = 0;
        for (int o = 0; o < 4; o++)
        {
            if (o == s) continue;
            int weight = riichi[o] ? 2 : (meldCount[o] >= 3 ? 1 : 0);
            if (weight == 0) continue;
            if (riichi[o]) riichiCount++;
            for (int x = 0; x < 34; x++) if (tc[x] > 0) botDanger[x] += weight * BotTileDanger(o, x);
        }
        return riichiCount;
    }

    /// Rough chance (0..14) that tile type x deals into seat o.
    int BotTileDanger(int o, int x)
    {
        if (BotInPond(o, x)) return 0;
        int seen = 4 - botUnseen[x];
        if (x >= 27)
        {
            if (seen >= 3) return 1;
            if (seen == 2) return 3;
            return IsYakuhaiFor(o, x) ? 8 : 6;
        }
        int r = x % 9;
        bool lowSuji = r >= 3 && BotInPond(o, x - 3);
        bool highSuji = r <= 5 && BotInPond(o, x + 3);
        int d;
        if (r == 0) d = highSuji ? 2 : 6;
        else if (r == 8) d = lowSuji ? 2 : 6;
        else if (r == 1) d = highSuji ? 3 : 8;
        else if (r == 7) d = lowSuji ? 3 : 8;
        else if (r == 2) d = highSuji ? 4 : 9;
        else if (r == 6) d = lowSuji ? 4 : 9;
        else d = (lowSuji && highSuji) ? 4 : ((lowSuji || highSuji) ? 8 : 12);
        if (seen >= 3) d -= 2;
        for (int k = 0; k < 1 + kanCount; k++) if (rules.DoraFromIndicator(wall[122 + k] / 4) == x) d += 2;
        return d < 1 ? 1 : d;
    }

    bool BotInPond(int o, int x)
    {
        for (int i = 0; i < discardCount[o]; i++)
        {
            int d = discards[o * DiscardSlots + i];
            if (d >= 0 && d / 4 == x) return true;
        }
        return false;
    }

    /// Dora value lost by discarding a tile of type x (counting a red five only when it's the last copy).
    int BotDoraValue(int s, int x)
    {
        int v = 0;
        for (int d = 0; d < 1 + kanCount; d++) if (rules.DoraFromIndicator(wall[122 + d] / 4) == x) v++;
        if (x < 27 && x % 9 == 4 && tc[x] == 1 && rules.IsAka(BotTileOfType(s, x))) v++;
        return v;
    }

    /// With x removed from tc (tenpai), would the bot be furiten: a wait already in its pond, or x itself a wait?
    bool BotFuritenAfter(int s, int x)
    {
        for (int t = 0; t < 34; t++)
        {
            if (!rules.IsWait(tc, meldCount[s], t)) continue;
            if (t == x || BotInPond(s, t)) return true;
        }
        return false;
    }

    bool IsYakuhaiFor(int s, int x)
    {
        return x >= 31 || x == 27 + SeatWind(s) || x == 27 + roundWind;
    }

    bool BotHasYakuhaiMeld(int s)
    {
        for (int m = 0; m < meldCount[s]; m++)
        {
            int k = meldKinds[s * 4 + m];
            if (k != MahjongRules.MeldChi && IsYakuhaiFor(s, melds[s * MeldSlots + m * 4] / 4)) return true;
        }
        return false;
    }

    /// A tile id of type t from the bot's hand: the drawn tile if it matches, else a non-red copy.
    int BotTileOfType(int s, int t)
    {
        if (drawn[s] >= 0 && drawn[s] / 4 == t) return drawn[s];
        int pick = -1;
        for (int k = 0; k < HandSlots; k++)
        {
            int id = hands[s * HandSlots + k];
            if (id < 0 || id / 4 != t) continue;
            if (pick < 0 || rules.IsAka(pick)) pick = id;
        }
        return pick;
    }

    int BotFirstType(int s)
    {
        for (int k = 0; k < HandSlots; k++) if (hands[s * HandSlots + k] >= 0) return hands[s * HandSlots + k] / 4;
        return 0;
    }

    void Hint(string msg)
    {
        Refresh();
        if (statusText != null) statusText.text = "<color=#ffcc66>" + msg + "</color>\n" + statusText.text;
    }

    // ================= display =================

    void Refresh()
    {
        bool inHand = phase != PhaseLobby;
        bool reveal = phase == PhaseHandOver || phase == PhaseGameOver;
        if (phase != PhaseTurn) riichiArmed = false;
        if (phase != PhaseCalls && phase != PhaseTurn) selectedType = -1;
        if (phase == PhaseTurn && ControlsSeat(turn)) UpdateTurnHints();
        else hintTsumo = false;
        discardHintTint = CallHintTint();

        for (int s = 0; s < 4; s++)
        {
            RefreshHand(s, inHand, reveal || ControlsSeat(s));
            RefreshDiscards(s, inHand);
            RefreshMelds(s, inHand);
            RefreshLabel(s, inHand);
        }

        if (doraTiles != null)
        {
            for (int d = 0; d < doraTiles.Length; d++)
            {
                if (doraTiles[d] == null) continue;
                if (!inHand) doraTiles[d].Hide();
                else if (d < 1 + kanCount) doraTiles[d].ShowFace(wall[122 + d], 0);
                else doraTiles[d].ShowBack();
            }
        }

        RefreshAnnouncer();
        UpdateButtons();
        if (statusText != null) statusText.text = BuildStatus();
    }

    // ================= context buttons =================

    /// Called by a context button. Runs whatever that slot is showing for the local player.
    public void OnActionButton(int seat, int slot)
    {
        if (seat < 0 || seat > 3 || slot < 0 || slot >= ButtonSlots || !ControlsSeat(seat)) return;
        int code = slotCode[seat * ButtonSlots + slot];
        int arg = slotArg[seat * ButtonSlots + slot];
        if (code == BtnTsumo) ActTsumo();
        else if (code == BtnRiichi) ActRiichi();
        else if (code == BtnOwnKan) { if (TurnSeat() >= 0) Send(turn, ActOwnKan, arg); }
        else if (code == BtnRon) Send(seat, ActRonWin, 0);
        else if (code == BtnPon) Send(seat, ActPonCall, 0);
        else if (code == BtnChi) Send(seat, ActChiCall, arg);
        else if (code == BtnCallKan) Send(seat, ActKanCall, 0);
        else if (code == BtnPass) Send(seat, ActPassCall, 0);
        else if (code == BtnStart) ActStart();
        else if (code == BtnNext) ActNextHand();
        else if (code == BtnReset) ActReset();
    }

    void UpdateButtons()
    {
        if (actionButtons == null) return;
        for (int s = 0; s < 4; s++)
        {
            slotFill = 0;
            if (ControlsSeat(s)) AddSeatButtons(s);
            for (int k = slotFill; k < ButtonSlots; k++) SetSlot(s, k, BtnNone, 0, "");
        }
    }

    void AddSeatButtons(int s)
    {
        if (phase == PhaseLobby) { AddButton(s, BtnStart, 0, "Start"); return; }
        if (phase == PhaseGameOver) { AddButton(s, BtnStart, 0, "New Game"); return; }
        if (phase == PhaseHandOver)
        {
            AddButton(s, BtnNext, 0, "Next Hand");
            AddButton(s, BtnReset, 0, "Reset");
            return;
        }
        if (phase == PhaseTurn && s == turn)
        {
            if (hintTsumo) AddButton(s, BtnTsumo, 0, "Tsumo");
            if (AnyHint(hintRiichi)) AddButton(s, BtnRiichi, 0, riichiArmed ? "Cancel Riichi" : "Riichi");
            for (int t = 0; t < 34; t++) if (hintKan[t]) AddButton(s, BtnOwnKan, t, "Kan " + TypeName(t));
            return;
        }
        if (phase == PhaseCalls && callResponse[s] == RespPending)
        {
            int opts = callOptions[s];
            if ((opts & OptRon) != 0) AddButton(s, BtnRon, 0, "Ron");
            if ((opts & OptPon) != 0) AddButton(s, BtnPon, 0, "Pon");
            if ((opts & OptKan) != 0) AddButton(s, BtnCallKan, 0, "Kan");
            if ((opts & OptChi) != 0)
            {
                // One button per way to chi, so you pick the sequence directly.
                CountClosed(s, tc);
                int t = lastDiscard / 4;
                for (int st = t - 2; st <= t; st++) if (ChiPossible(st, t)) AddButton(s, BtnChi, st, "Chi " + SequenceName(st));
            }
            AddButton(s, BtnPass, 0, "Pass");
        }
    }

    void AddButton(int s, int code, int arg, string text)
    {
        if (slotFill >= ButtonSlots) return;
        SetSlot(s, slotFill, code, arg, text);
        slotFill++;
    }

    void SetSlot(int s, int k, int code, int arg, string text)
    {
        int i = s * ButtonSlots + k;
        slotCode[i] = code;
        slotArg[i] = arg;
        if (i < actionButtons.Length && actionButtons[i] != null) actionButtons[i].Show(text);
    }

    string TypeName(int t)
    {
        if (t < 27) return (t % 9 + 1) + SuitLetter(t);
        if (t == 27) return "East";
        if (t == 28) return "South";
        if (t == 29) return "West";
        if (t == 30) return "North";
        if (t == 31) return "White";
        if (t == 32) return "Green";
        return "Red";
    }

    string SequenceName(int st)
    {
        int n = st % 9 + 1;
        return n + "-" + (n + 1) + "-" + (n + 2) + SuitLetter(st);
    }

    string SuitLetter(int t)
    {
        int suit = t / 9;
        return suit == 0 ? "m" : (suit == 1 ? "p" : "s");
    }

    /// Owner: queue a big announcement that every client shows for announceSeconds.
    void Announce(string text, int seat)
    {
        announcement = seat >= 0 ? text + "\n<size=40%>" + SeatName(seat) + "</size>" : text;
        announceSerial++;
    }

    void RefreshAnnouncer()
    {
        if (announceText != null && announceSerial != shownAnnounceSerial)
        {
            shownAnnounceSerial = announceSerial;
            announceText.text = announcement;
            announceHideTime = Time.time + announceSeconds;
        }

        // The last discard, shown large while it's still on the table (it lights up if you can call it).
        bool show = (phase == PhaseTurn || phase == PhaseCalls || phase == PhasePause) && lastDiscard >= 0 && lastDiscarder >= 0
            && discardCount[lastDiscarder] > 0
            && (discardFlags[lastDiscarder * DiscardSlots + discardCount[lastDiscarder] - 1] & 2) == 0;
        if (announceTile != null)
        {
            if (show) announceTile.ShowFace(lastDiscard, discardHintTint);
            else announceTile.Hide();
        }
        if (announceCaption != null) announceCaption.text = show ? SeatName(lastDiscarder) + " discarded" : "";
    }

    void RefreshHand(int s, bool inHand, bool faceUp)
    {
        int b = s * HandSlots;
        for (int k = 0; k < HandSlots; k++) displayIds[b + k] = -1;
        if (inHand)
        {
            // Sorted closed tiles, with a freshly drawn tile kept apart in the last slot.
            int n = 0;
            for (int k = 0; k < HandSlots; k++)
            {
                int id = hands[b + k];
                if (id < 0 || id == drawn[s]) continue;
                int j = n;
                while (j > 0 && displayIds[b + j - 1] > id) { displayIds[b + j] = displayIds[b + j - 1]; j--; }
                displayIds[b + j] = id;
                n++;
            }
            if (drawn[s] >= 0) displayIds[b + HandSlots - 1] = drawn[s];
        }

        if (handTiles == null) return;
        bool locked = riichi[s] && phase == PhaseTurn && s == turn;
        bool myTurn = phase == PhaseTurn && s == turn && ControlsSeat(s);
        bool myCall = phase == PhaseCalls && callResponse[s] == RespPending && ControlsSeat(s);
        if (myCall) UpdateCallHints(s);
        for (int k = 0; k < HandSlots; k++)
        {
            MahjongTile tile = b + k < handTiles.Length ? handTiles[b + k] : null;
            if (tile == null) continue;
            int id = displayIds[b + k];
            if (id < 0) tile.Hide();
            else if (!faceUp) tile.ShowBack();
            else
            {
                int tint = 0;
                int t = id / 4;
                if (t == selectedType && ControlsSeat(s)) tint = 1;
                else if (myTurn && hintTsumo && id == drawn[s]) tint = 4;
                else if (myTurn && (hintKan[t] || hintRiichi[t])) tint = 3;
                else if (myCall && hintCall[t]) tint = 3;
                else if (locked && id != drawn[s]) tint = 2;
                tile.ShowFace(id, tint);
            }
        }
    }

    void RefreshDiscards(int s, bool inHand)
    {
        if (discardTiles == null) return;
        for (int k = 0; k < DiscardSlots; k++)
        {
            int i = s * DiscardSlots + k;
            if (i >= discardTiles.Length || discardTiles[i] == null) continue;
            int id = inHand && k < discardCount[s] ? discards[i] : -1;
            if (id < 0) { discardTiles[i].Hide(); continue; }
            int f = discardFlags[i];
            int tint = (f & 2) != 0 ? 2 : ((f & 1) != 0 ? 5 : 0);
            if (s == lastDiscarder && k == discardCount[s] - 1 && (f & 2) == 0 && phase != PhaseHandOver && phase != PhaseGameOver) tint = 1;
            if (discardHintTint != 0 && s == lastDiscarder && k == discardCount[s] - 1) tint = discardHintTint;
            discardTiles[i].ShowFace(id, tint);
        }
    }

    /// Works out which tiles to light up on the local player's turn: tsumo, kan and riichi discards.
    void UpdateTurnHints()
    {
        int key = ((((turn * 200 + wallPos) * 8 + kanCount) * 8 + meldCount[turn]) * 136) + wall[0];
        if (key == hintKey) return;
        hintKey = key;
        for (int x = 0; x < 34; x++) { hintRiichi[x] = false; hintKan[x] = false; }
        hintTsumo = CanLocalTsumo();

        int s = turn;
        if (rules == null || drawn[s] < 0 || riichi[s]) return;
        CountClosed(s, tc);
        if (kanCount < 4 && Remaining() > 0)
            for (int x = 0; x < 34; x++) hintKan[x] = tc[x] == 4 || (tc[x] >= 1 && PonMeldOf(s, x) >= 0);
        if (IsClosed(s) && scores[s] >= 1000 && Remaining() >= 4)
        {
            for (int x = 0; x < 34; x++)
            {
                if (tc[x] == 0) continue;
                tc[x]--;
                hintRiichi[x] = rules.IsTenpai(tc, meldCount[s]);
                tc[x]++;
            }
        }
    }

    /// Lights up the hand tiles a pon / kan / chi on the last discard would use.
    void UpdateCallHints(int s)
    {
        for (int x = 0; x < 34; x++) hintCall[x] = false;
        int opts = callOptions[s];
        int t = lastDiscard / 4;
        if ((opts & (OptPon | OptKan)) != 0) hintCall[t] = true;
        if ((opts & OptChi) != 0)
        {
            CountClosed(s, tc);
            for (int st = t - 2; st <= t; st++)
            {
                if (!ChiPossible(st, t)) continue;
                for (int x = st; x <= st + 2; x++) if (x != t) hintCall[x] = true;
            }
        }
    }

    /// The last discard lights up red when the local player can ron it, green when they can call it.
    int CallHintTint()
    {
        if (phase != PhaseCalls) return 0;
        int tint = 0;
        for (int s = 0; s < 4; s++)
        {
            if (callResponse[s] != RespPending || !ControlsSeat(s)) continue;
            if ((callOptions[s] & OptRon) != 0) return 4;
            if (callOptions[s] != 0) tint = 3;
        }
        return tint;
    }

    bool AnyHint(bool[] mask)
    {
        for (int x = 0; x < 34; x++) if (mask[x]) return true;
        return false;
    }

    void RefreshMelds(int s, bool inHand)
    {
        if (meldTiles == null) return;
        for (int k = 0; k < MeldSlots; k++)
        {
            int i = s * MeldSlots + k;
            if (i >= meldTiles.Length || meldTiles[i] == null) continue;
            int m = k / 4;
            int id = inHand && m < meldCount[s] ? melds[i] : -1;
            if (id < 0) meldTiles[i].Hide();
            else if (meldKinds[s * 4 + m] == MahjongRules.MeldClosedKan && (k % 4 == 0 || k % 4 == 3)) meldTiles[i].ShowBack();
            else meldTiles[i].ShowFace(id, 0);
        }
    }

    void RefreshLabel(int s, bool inHand)
    {
        if (seatLabels == null || s >= seatLabels.Length || seatLabels[s] == null) return;
        string name = seatPlayers[s] != -1 ? SeatName(s) : compassNames[s] + " (empty)";
        if (!inHand)
        {
            seatLabels[s].text = phase == PhaseLobby && scores[s] == 0 ? name : name + "\n" + scores[s];
            return;
        }
        string text = "[" + windNames[SeatWind(s)] + "] " + name + "\n" + scores[s];
        if (riichi[s]) text += "  <color=#ff6666>RIICHI</color>";
        bool active = (phase == PhaseTurn && s == turn) || (phase == PhaseCalls && callResponse[s] == RespPending);
        seatLabels[s].text = active ? "<color=#ffdd33>" + text + "</color>" : text;
    }

    string BuildStatus()
    {
        string s = "<b>RIICHI MAHJONG</b>";
        if (phase == PhaseLobby)
        {
            s += "\nClick Sit (or Bot) at each seat, then Start. (" + SeatedCount() + "/4 seated)";
        }
        else
        {
            s += "   " + RoundName() + "   Honba " + honba + "   Riichi sticks " + riichiSticks + "   Wall " + Remaining();
            s += "\n<size=75%>" + ScoreLine() + "</size>";
            if (phase == PhaseTurn)
            {
                s += "\n" + SeatName(turn) + " to discard.";
                if (ControlsSeat(turn))
                {
                    s += " Click a tile to discard.";
                    if (hintTsumo) s += "\n<color=#ff7755>TSUMO is available! (red tile)</color>";
                    if (AnyHint(hintRiichi)) s += "\n<color=#66ff66>Riichi possible: Riichi, then a green tile.</color>";
                    if (AnyHint(hintKan)) s += "\n<color=#66ff66>Kan possible.</color>";
                    if (riichiArmed) s += " <color=#ff6666>Riichi armed.</color>";
                }
            }
            else if (phase == PhaseCalls)
            {
                s += "\nWaiting on calls for " + SeatName(lastDiscarder) + "'s discard" + CountdownText() + ".";
                for (int i = 1; i < 4; i++)
                {
                    int c = (lastDiscarder + i) % 4;
                    if (callResponse[c] != RespPending || !ControlsSeat(c)) continue;
                    s += "\n<color=#66ff66>" + SeatName(c) + " can: " + OptionText(callOptions[c]) + " or Pass</color>";
                }
            }
            else if (phase == PhasePause) s += "\n" + SeatName((lastDiscarder + 1) % 4) + " draws next...";
            else if (phase == PhaseHandOver) s += nextHandSeconds > 0f ? "\nNext hand in " + Seconds() + "s (or press Next Hand)." : "\nHand over. Press Next Hand.";
            else if (phase == PhaseGameOver) s += "\nPress Start for a new game.";
        }
        if (!string.IsNullOrEmpty(lastEvent)) s += "\n<size=80%>" + lastEvent + "</size>";
        if (debugControlAllSeats) s += "\n<size=70%><color=#ff8888>Debug: controlling all seats</color></size>";
        return s;
    }

    /// "E Name 25000 · S Name 24000 ..." with the local player's own seat highlighted.
    string ScoreLine()
    {
        string line = "";
        for (int s = 0; s < 4; s++)
        {
            string part = windNames[SeatWind(s)].Substring(0, 1) + " " + SeatName(s) + " " + scores[s] + (riichi[s] ? " (R)" : "");
            if (s == LocalSeat()) part = "<color=#ffdd33>" + part + "</color>";
            line += (s > 0 ? "  ·  " : "") + part;
        }
        return line;
    }

    string CountdownText()
    {
        if (callWindowSeconds <= 0f) return "";
        return " (" + Seconds() + "s)";
    }

    /// Whole seconds left on the running timer.
    int Seconds()
    {
        int ms = timerDeadline - Networking.GetServerTimeInMilliseconds();
        return ms <= 0 ? 0 : (ms + 999) / 1000;
    }

    string OptionText(int opts)
    {
        string t = "";
        if ((opts & OptRon) != 0) t += "RON ";
        if ((opts & OptPon) != 0) t += "Pon ";
        if ((opts & OptKan) != 0) t += "Kan ";
        if ((opts & OptChi) != 0) t += "Chi ";
        return t.Trim();
    }

    bool CanLocalTsumo()
    {
        if (rules == null || !canTsumo || drawn[turn] < 0) return false;
        CountClosed(turn, tc);
        if (!rules.IsCompleteShape(tc, meldCount[turn])) return false;
        return Evaluate(turn, -1, drawn[turn], true);
    }
}
