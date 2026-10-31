using UdonSharp;

/// Riichi mahjong hand evaluation: tenpai / waits, yaku, han, fu and base points.
/// Pure logic (ints and arrays only) so it can be unit-tested outside Unity.
///
/// Tile ids are 0..135, tile type = id / 4:
///   0-8 man 1-9, 9-17 pin 1-9, 18-26 sou 1-9, 27-30 winds E S W N, 31-33 dragons white green red.
///   Ids 16, 52 and 88 are the red fives.
/// Meld kinds: 1 chi, 2 pon, 3 open kan, 4 closed kan, 5 added kan. Each meld uses 4 id slots (-1 when unused).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MahjongRules : UdonSharpBehaviour
{
    public const int MeldChi = 1;
    public const int MeldPon = 2;
    public const int MeldOpenKan = 3;
    public const int MeldClosedKan = 4;
    public const int MeldAddedKan = 5;

    // ---- results of the last successful EvaluateWin ----
    [System.NonSerialized] public int resultHan;
    [System.NonSerialized] public int resultFu;
    [System.NonSerialized] public int resultYakuman;
    [System.NonSerialized] public int resultBase;
    [System.NonSerialized] public string resultText = "";

    // ---- evaluation state ----
    int[] cnt = new int[34];      // closed tiles still to decompose
    int[] all = new int[34];      // every tile in the hand, melds included
    int[] tmp = new int[34];
    int[] setKind = new int[4];   // 0 sequence, 1 triplet, 2 kan
    int[] setStart = new int[4];
    bool[] setOpen = new bool[4];
    bool[] setFromMeld = new bool[4];
    int setN;
    int meldSets;
    int pairT;

    int winT;
    bool isTsumo;
    bool menzen;
    int seatWindT;
    int roundWindT;
    bool hasRiichi;
    bool hasIppatsu;
    bool lastTile;
    bool rinshan;
    bool hasDoubleRiichi;
    int heavenly;          // 1 tenhou (dealer's first draw), 2 chiihou (non-dealer's first draw, no calls yet)
    bool robbedKan;        // chankan
    int doraHan;

    int bestBase;
    int bestHan;
    int bestFu;
    int bestYakuman;
    string bestText;

    // ================= shape checks =================

    /// True if closed counts (14 - 3 * meldCount tiles) form a complete hand shape.
    public bool IsCompleteShape(int[] c, int meldCount)
    {
        if (meldCount == 0 && (IsChiitoi(c) || IsKokushi(c))) return true;
        for (int p = 0; p < 34; p++)
        {
            if (c[p] < 2) continue;
            for (int i = 0; i < 34; i++) tmp[i] = c[i];
            tmp[p] -= 2;
            if (GreedySets(tmp)) return true;
        }
        return false;
    }

    bool GreedySets(int[] t)
    {
        for (int i = 0; i < 34; i++)
        {
            if (t[i] >= 3) t[i] -= 3;
            while (t[i] > 0)
            {
                if (i < 27 && i % 9 <= 6 && t[i + 1] > 0 && t[i + 2] > 0)
                {
                    t[i]--; t[i + 1]--; t[i + 2]--;
                }
                else return false;
            }
        }
        return true;
    }

    bool IsChiitoi(int[] c)
    {
        int pairs = 0;
        for (int i = 0; i < 34; i++)
        {
            if (c[i] == 2) pairs++;
            else if (c[i] != 0) return false;
        }
        return pairs == 7;
    }

    bool IsKokushi(int[] c)
    {
        bool pair = false;
        for (int i = 0; i < 34; i++)
        {
            if (IsTermOrHonor(i))
            {
                if (c[i] == 0) return false;
                if (c[i] == 2) { if (pair) return false; pair = true; }
                else if (c[i] != 1) return false;
            }
            else if (c[i] != 0) return false;
        }
        return pair;
    }

    /// True if adding tile type t to the closed counts (13 - 3 * meldCount tiles) completes the shape.
    public bool IsWait(int[] c, int meldCount, int t)
    {
        if (c[t] >= 4) return false;
        c[t]++;
        bool ok = IsCompleteShape(c, meldCount);
        c[t]--;
        return ok;
    }

    public bool IsTenpai(int[] c, int meldCount)
    {
        // A winning tile always touches the hand (same tile, or within 2 in its suit), except kokushi's missing tile.
        bool kokushiLike = meldCount == 0 && TermHonorKinds(c) >= 12;
        for (int t = 0; t < 34; t++)
        {
            if (!kokushiLike && !Touches(c, t)) continue;
            if (IsWait(c, meldCount, t)) return true;
        }
        return false;
    }

    /// Marks in waits[] every tile type that completes the closed counts (13 - 3 * meldCount tiles) and returns
    /// how many there are.
    public int FindWaits(int[] c, int meldCount, bool[] waits)
    {
        bool kokushiLike = meldCount == 0 && TermHonorKinds(c) >= 12;
        int n = 0;
        for (int t = 0; t < 34; t++)
        {
            waits[t] = (kokushiLike || Touches(c, t)) && IsWait(c, meldCount, t);
            if (waits[t]) n++;
        }
        return n;
    }

    bool Touches(int[] c, int t)
    {
        if (c[t] > 0) return true;
        if (t >= 27) return false;
        int r = t % 9;
        if (r >= 1 && c[t - 1] > 0) return true;
        if (r >= 2 && c[t - 2] > 0) return true;
        if (r <= 7 && c[t + 1] > 0) return true;
        if (r <= 6 && c[t + 2] > 0) return true;
        return false;
    }

    int TermHonorKinds(int[] c)
    {
        int n = 0;
        for (int t = 0; t < 34; t++) if (c[t] > 0 && IsTermOrHonor(t)) n++;
        return n;
    }

    // ================= full evaluation =================

    /// Scores a complete hand. hand[handOffset .. handOffset+14) are the closed ids (-1 = empty); extraId is the
    /// ron tile (or -1 for tsumo, where the drawn tile is already in hand). Returns false if there is no yaku.
    /// doubleRiichi: riichi was declared on the first discard. firstDraw: tsumo on the player's very first draw
    /// with no calls yet (tenhou for the dealer, chiihou otherwise). chankan: ron on a tile added to a kan.
    public bool EvaluateWin(int[] hand, int handOffset, int extraId,
        int[] meldIds, int[] meldKinds, int seat, int meldCount,
        int winId, bool tsumo, int seatWind, int roundWind,
        bool riichi, bool ippatsu, bool last, bool afterKan,
        int[] wall, int doraCount, bool doubleRiichi, bool firstDraw, bool chankan)
    {
        hasDoubleRiichi = riichi && doubleRiichi;
        heavenly = firstDraw && tsumo && meldCount == 0 ? (seatWind == 0 ? 1 : 2) : 0;
        robbedKan = chankan && !tsumo;
        for (int i = 0; i < 34; i++) { cnt[i] = 0; all[i] = 0; }
        int aka = 0;
        for (int k = 0; k < 14; k++)
        {
            int id = hand[handOffset + k];
            if (id < 0) continue;
            cnt[id / 4]++;
            if (IsAka(id)) aka++;
        }
        if (extraId >= 0)
        {
            cnt[extraId / 4]++;
            if (IsAka(extraId)) aka++;
        }
        for (int i = 0; i < 34; i++) all[i] = cnt[i];

        menzen = true;
        meldSets = 0;
        for (int m = 0; m < meldCount; m++)
        {
            int kind = meldKinds[seat * 4 + m];
            int lowest = 99;
            for (int k = 0; k < 4; k++)
            {
                int id = meldIds[seat * 16 + m * 4 + k];
                if (id < 0) continue;
                all[id / 4]++;
                if (IsAka(id)) aka++;
                if (id / 4 < lowest) lowest = id / 4;
            }
            setStart[m] = lowest;
            setFromMeld[m] = true;
            setOpen[m] = kind != MeldClosedKan;
            if (kind == MeldChi) setKind[m] = 0;
            else if (kind == MeldPon) setKind[m] = 1;
            else setKind[m] = 2;
            if (kind != MeldClosedKan) menzen = false;
            meldSets++;
        }

        winT = winId / 4;
        isTsumo = tsumo;
        seatWindT = 27 + seatWind;
        roundWindT = 27 + roundWind;
        hasRiichi = riichi;
        hasIppatsu = ippatsu;
        lastTile = last;
        rinshan = afterKan;

        // Dora are counted once per hand; they only count if the hand has a yaku.
        doraHan = aka;
        for (int d = 0; d < doraCount; d++)
        {
            doraHan += all[DoraFromIndicator(wall[122 + d] / 4)];
            if (riichi) doraHan += all[DoraFromIndicator(wall[127 + d] / 4)];
        }

        bestBase = 0;
        bestHan = 0;
        bestFu = 0;
        bestYakuman = 0;
        bestText = "";

        if (meldCount == 0 && IsKokushi(cnt)) ScoreKokushi();
        if (meldCount == 0 && IsChiitoi(cnt)) ScoreChiitoi();

        for (int p = 0; p < 34; p++)
        {
            if (cnt[p] < 2) continue;
            cnt[p] -= 2;
            pairT = p;
            setN = meldSets;
            Decompose();
            cnt[p] += 2;
        }

        if (bestBase <= 0) return false;
        resultHan = bestHan;
        resultFu = bestFu;
        resultYakuman = bestYakuman;
        resultBase = bestBase;
        resultText = bestText;
        return true;
    }

    [RecursiveMethod]
    void Decompose()
    {
        int i = 0;
        while (i < 34 && cnt[i] == 0) i++;
        if (i == 34)
        {
            if (setN == 4) ScorePlacements();
            return;
        }
        if (setN >= 4) return;

        if (cnt[i] >= 3)
        {
            cnt[i] -= 3;
            setKind[setN] = 1; setStart[setN] = i; setOpen[setN] = false; setFromMeld[setN] = false;
            setN++;
            Decompose();
            setN--;
            cnt[i] += 3;
        }
        if (i < 27 && i % 9 <= 6 && cnt[i + 1] > 0 && cnt[i + 2] > 0)
        {
            cnt[i]--; cnt[i + 1]--; cnt[i + 2]--;
            setKind[setN] = 0; setStart[setN] = i; setOpen[setN] = false; setFromMeld[setN] = false;
            setN++;
            Decompose();
            setN--;
            cnt[i]++; cnt[i + 1]++; cnt[i + 2]++;
        }
    }

    /// The winning tile may complete the pair or any closed set containing it; each gives a different wait.
    void ScorePlacements()
    {
        if (pairT == winT) ScoreStandard(-1, 3);
        for (int s = 0; s < 4; s++)
        {
            if (setFromMeld[s]) continue;
            int st = setStart[s];
            if (setKind[s] == 1)
            {
                if (st == winT) ScoreStandard(s, 4);
            }
            else if (winT >= st && winT <= st + 2)
            {
                int wait;
                if (winT == st + 1) wait = 1;
                else if (winT == st) wait = (st % 9 == 6) ? 2 : 0;
                else wait = (st % 9 == 0) ? 2 : 0;
                ScoreStandard(s, wait);
            }
        }
    }

    /// wait: 0 ryanmen, 1 kanchan, 2 penchan, 3 tanki, 4 shanpon.
    void ScoreStandard(int winSet, int wait)
    {
        string text = "";
        int han = 0;
        int yakuman = 0;

        int seqs = 0, trips = 0, kans = 0, concealedTrips = 0;
        int dragonTrips = 0, windTrips = 0;
        int yakuhai = 0;
        bool allHaveTerminal = true;
        for (int s = 0; s < 4; s++)
        {
            int st = setStart[s];
            if (setKind[s] == 0)
            {
                seqs++;
                if (st % 9 != 0 && st % 9 != 6) allHaveTerminal = false;
                continue;
            }
            trips++;
            if (setKind[s] == 2) kans++;
            bool open = setOpen[s] || (s == winSet && !isTsumo);
            if (!open) concealedTrips++;
            if (!IsTermOrHonor(st)) allHaveTerminal = false;
            if (st >= 31) { dragonTrips++; yakuhai++; }
            if (st >= 27 && st <= 30) windTrips++;
            if (st == seatWindT) yakuhai++;
            if (st == roundWindT) yakuhai++;
        }
        if (!IsTermOrHonor(pairT)) allHaveTerminal = false;

        bool pairYakuhai = pairT >= 31 || pairT == seatWindT || pairT == roundWindT;
        bool pinfu = menzen && seqs == 4 && !pairYakuhai && wait == 0;

        int suitMask = 0;
        bool hasHonor = false, allSimple = true, allTermHonor = true, allHonor = true, allGreen = true, allTerminal = true;
        for (int t = 0; t < 34; t++)
        {
            if (all[t] == 0) continue;
            if (t >= 27) hasHonor = true; else suitMask |= 1 << (t / 9);
            if (IsTermOrHonor(t)) allSimple = false; else allTermHonor = false;
            if (t < 27) allHonor = false;
            if (t >= 27 || (t % 9 != 0 && t % 9 != 8)) allTerminal = false;
            if (t != 19 && t != 20 && t != 21 && t != 23 && t != 25 && t != 32) allGreen = false;
        }
        int suits = (suitMask & 1) + ((suitMask >> 1) & 1) + ((suitMask >> 2) & 1);

        // ---- yakuman ----
        if (concealedTrips == 4) { yakuman++; text = Add(text, "Suuankou", 13); }
        if (dragonTrips == 3) { yakuman++; text = Add(text, "Daisangen", 13); }
        if (windTrips == 4) { yakuman++; text = Add(text, "Daisuushii", 13); }
        else if (windTrips == 3 && pairT >= 27 && pairT <= 30) { yakuman++; text = Add(text, "Shousuushii", 13); }
        if (allHonor) { yakuman++; text = Add(text, "Tsuuiisou", 13); }
        if (allTerminal) { yakuman++; text = Add(text, "Chinroutou", 13); }
        if (allGreen) { yakuman++; text = Add(text, "Ryuuiisou", 13); }
        if (kans == 4) { yakuman++; text = Add(text, "Suukantsu", 13); }
        if (meldSets == 0 && suits == 1 && !hasHonor && IsChuuren()) { yakuman++; text = Add(text, "Chuuren Poutou", 13); }
        if (heavenly > 0) { yakuman++; text = Add(text, HeavenlyName(), 13); }

        if (yakuman > 0)
        {
            Consider(8000 * yakuman, 13 * yakuman, 0, yakuman, text);
            return;
        }

        // ---- regular yaku ----
        han += CommonYaku();
        text = CommonText();
        if (pinfu) { han += 1; text = Add(text, "Pinfu", 1); }
        if (allSimple) { han += 1; text = Add(text, "Tanyao", 1); }
        if (yakuhai > 0)
        {
            han += yakuhai;
            for (int s = 0; s < 4; s++)
            {
                if (setKind[s] == 0) continue;
                int st = setStart[s];
                if (st == 31) text = Add(text, "Haku", 1);
                else if (st == 32) text = Add(text, "Hatsu", 1);
                else if (st == 33) text = Add(text, "Chun", 1);
                if (st == seatWindT) text = Add(text, "Seat Wind", 1);
                if (st == roundWindT) text = Add(text, "Round Wind", 1);
            }
        }

        if (menzen)
        {
            int peikou = Peikou();
            if (peikou >= 2) { han += 3; text = Add(text, "Ryanpeikou", 3); }
            else if (peikou == 1) { han += 1; text = Add(text, "Iipeikou", 1); }
        }

        int op = menzen ? 0 : 1; // open hands lose 1 han on some yaku
        if (HasSanshokuSeq()) { han += 2 - op; text = Add(text, "Sanshoku", 2 - op); }
        if (HasIttsu()) { han += 2 - op; text = Add(text, "Ittsu", 2 - op); }
        if (seqs == 0) { han += 2; text = Add(text, "Toitoi", 2); }
        if (concealedTrips == 3) { han += 2; text = Add(text, "Sanankou", 2); }
        if (kans == 3) { han += 2; text = Add(text, "Sankantsu", 2); }
        if (HasSanshokuTrip()) { han += 2; text = Add(text, "Sanshoku Doukou", 2); }
        if (dragonTrips == 2 && pairT >= 31) { han += 2; text = Add(text, "Shousangen", 2); }

        if (allTermHonor) { han += 2; text = Add(text, "Honroutou", 2); }
        else if (allHaveTerminal && seqs > 0)
        {
            if (hasHonor) { han += 2 - op; text = Add(text, "Chanta", 2 - op); }
            else { han += 3 - op; text = Add(text, "Junchan", 3 - op); }
        }

        if (suits == 1 && !hasHonor) { han += 6 - op; text = Add(text, "Chinitsu", 6 - op); }
        else if (suits == 1 && hasHonor) { han += 3 - op; text = Add(text, "Honitsu", 3 - op); }

        if (han == 0) return; // no yaku

        // ---- fu ----
        int fu;
        if (pinfu) fu = isTsumo ? 20 : 30;
        else
        {
            fu = 20;
            if (menzen && !isTsumo) fu += 10;
            if (isTsumo) fu += 2;
            for (int s = 0; s < 4; s++)
            {
                if (setKind[s] == 0) continue;
                int f = IsTermOrHonor(setStart[s]) ? 4 : 2;
                bool open2 = setOpen[s] || (s == winSet && !isTsumo);
                if (!open2) f *= 2;
                if (setKind[s] == 2) f *= 4;
                fu += f;
            }
            if (pairT >= 31) fu += 2;
            if (pairT == seatWindT) fu += 2;
            if (pairT == roundWindT) fu += 2;
            if (wait == 1 || wait == 2 || wait == 3) fu += 2;
            fu = ((fu + 9) / 10) * 10;
            if (fu < 30) fu = 30;
        }

        han += doraHan;
        if (doraHan > 0) text = Add(text, "Dora", doraHan);
        Consider(BasePoints(han, fu), han, fu, 0, text);
    }

    void ScoreChiitoi()
    {
        bool allSimple = true, allTermHonor = true, hasHonor = false, allHonor = true;
        int suitMask = 0;
        for (int t = 0; t < 34; t++)
        {
            if (all[t] == 0) continue;
            if (IsTermOrHonor(t)) allSimple = false; else allTermHonor = false;
            if (t >= 27) hasHonor = true; else { suitMask |= 1 << (t / 9); allHonor = false; }
        }
        if (allHonor || heavenly > 0)
        {
            int ym = 0;
            string yt = "";
            if (allHonor) { ym++; yt = Add(yt, "Tsuuiisou", 13); }
            if (heavenly > 0) { ym++; yt = Add(yt, HeavenlyName(), 13); }
            Consider(8000 * ym, 13 * ym, 0, ym, yt);
            return;
        }
        int suits = (suitMask & 1) + ((suitMask >> 1) & 1) + ((suitMask >> 2) & 1);
        int han = 2 + CommonYaku();
        string text = Add(CommonText(), "Chiitoitsu", 2);
        if (allSimple) { han += 1; text = Add(text, "Tanyao", 1); }
        if (allTermHonor) { han += 2; text = Add(text, "Honroutou", 2); }
        if (suits == 1 && !hasHonor) { han += 6; text = Add(text, "Chinitsu", 6); }
        else if (suits == 1) { han += 3; text = Add(text, "Honitsu", 3); }
        han += doraHan;
        if (doraHan > 0) text = Add(text, "Dora", doraHan);
        Consider(BasePoints(han, 25), han, 25, 0, text);
    }

    void ScoreKokushi()
    {
        if (heavenly > 0) Consider(16000, 26, 0, 2, "Kokushi Musou, " + HeavenlyName());
        else Consider(8000, 13, 0, 1, "Kokushi Musou");
    }

    string HeavenlyName()
    {
        return heavenly == 1 ? "Tenhou" : "Chiihou";
    }

    /// Yaku that don't depend on the decomposition.
    int CommonYaku()
    {
        int han = 0;
        if (hasDoubleRiichi) han += 2;
        else if (hasRiichi) han++;
        if (hasIppatsu) han++;
        if (menzen && isTsumo) han++;
        if (lastTile) han++;
        if (rinshan) han++;
        if (robbedKan) han++;
        return han;
    }

    string CommonText()
    {
        string text = "";
        if (hasDoubleRiichi) text = Add(text, "Double Riichi", 2);
        else if (hasRiichi) text = Add(text, "Riichi", 1);
        if (hasIppatsu) text = Add(text, "Ippatsu", 1);
        if (menzen && isTsumo) text = Add(text, "Menzen Tsumo", 1);
        if (lastTile) text = Add(text, isTsumo ? "Haitei" : "Houtei", 1);
        if (rinshan) text = Add(text, "Rinshan Kaihou", 1);
        if (robbedKan) text = Add(text, "Chankan", 1);
        return text;
    }

    void Consider(int basePts, int han, int fu, int yakuman, string text)
    {
        if (basePts > bestBase || (basePts == bestBase && han > bestHan))
        {
            bestBase = basePts;
            bestHan = han;
            bestFu = fu;
            bestYakuman = yakuman;
            bestText = text;
        }
    }

    int Peikou()
    {
        int peikou = 0;
        bool[] used = new bool[4];
        for (int a = 0; a < 4; a++)
        {
            if (used[a] || setKind[a] != 0) continue;
            for (int b = a + 1; b < 4; b++)
            {
                if (used[b] || setKind[b] != 0 || setStart[b] != setStart[a]) continue;
                used[a] = true;
                used[b] = true;
                peikou++;
                break;
            }
        }
        return peikou;
    }

    bool HasSeq(int start)
    {
        for (int s = 0; s < 4; s++) if (setKind[s] == 0 && setStart[s] == start) return true;
        return false;
    }

    bool HasTrip(int t)
    {
        for (int s = 0; s < 4; s++) if (setKind[s] != 0 && setStart[s] == t) return true;
        return false;
    }

    bool HasSanshokuSeq()
    {
        for (int n = 0; n < 7; n++) if (HasSeq(n) && HasSeq(n + 9) && HasSeq(n + 18)) return true;
        return false;
    }

    bool HasIttsu()
    {
        for (int b = 0; b < 27; b += 9) if (HasSeq(b) && HasSeq(b + 3) && HasSeq(b + 6)) return true;
        return false;
    }

    bool HasSanshokuTrip()
    {
        for (int n = 0; n < 9; n++) if (HasTrip(n) && HasTrip(n + 9) && HasTrip(n + 18)) return true;
        return false;
    }

    bool IsChuuren()
    {
        int b = -1;
        for (int t = 0; t < 27; t++) if (all[t] > 0) { b = t - t % 9; break; }
        if (b < 0) return false;
        if (all[b] < 3 || all[b + 8] < 3) return false;
        for (int k = 1; k < 8; k++) if (all[b + k] < 1) return false;
        return true;
    }

    // ================= shanten (used by the bots) =================
    // Shanten = tiles away from tenpai: 0 is tenpai, -1 is a complete hand.
    // Suits never share sets, so each suit (and the honors) is searched on its own and the results combined.
    // groupTable[g * 10 + pair * 5 + m] = most partial sets group g can form alongside m sets, -1 = impossible.

    int[] groupTable = new int[40];
    int[] spareTable = new int[10];
    int[] gc = new int[9];
    int scanLen;
    bool scanHonor;
    // Memo for Solve: slot (i * 5 + a) * 5 + b holds a 10-entry table like groupTable; slot 225 is the empty end.
    int[] memoTab = new int[2260];
    int[] memoStamp = new int[226];
    int stamp = 0;
    const int CacheSize = 509;
    int[] cacheKeys = new int[509];
    int[] cacheTabs = new int[5090];
    int[] dpA = new int[10];
    int[] dpB = new int[10];

    /// Shanten of the closed counts c (13 or 14 tiles minus 3 per meld).
    public int Shanten(int[] c, int meldCount)
    {
        for (int g = 0; g < 4; g++) BuildGroup(c, g, groupTable, g * 10);
        return BestShanten(c, meldCount, -1);
    }

    /// Number of unseen tiles that would lower the shanten of c (a 13-tile style hand).
    /// unseen[t] = copies of type t the bot hasn't seen. Leaves c unchanged.
    public int Ukeire(int[] c, int meldCount, int[] unseen)
    {
        int baseShanten = Shanten(c, meldCount);
        int total = 0;
        for (int t = 0; t < 34; t++)
        {
            if (unseen[t] <= 0 || c[t] >= 4) continue;
            // A tile that touches nothing can't form a set, so only seven pairs / thirteen orphans can change.
            bool touches = Touches(c, t);
            c[t]++;
            int s;
            if (touches)
            {
                BuildGroup(c, t / 9, spareTable, 0);
                s = BestShanten(c, meldCount, t / 9);
            }
            else s = SpecialShanten(c, meldCount, StandardShanten(meldCount, -1));
            c[t]--;
            if (s < baseShanten) total += unseen[t];
        }
        return total;
    }

    int BestShanten(int[] c, int meldCount, int swapGroup)
    {
        return SpecialShanten(c, meldCount, StandardShanten(meldCount, swapGroup));
    }

    /// Takes the min of the standard shanten with seven pairs and thirteen orphans (closed hands only).
    int SpecialShanten(int[] c, int meldCount, int best)
    {
        if (meldCount > 0) return best;
        int pairs = 0, kinds = 0, orphanKinds = 0;
        bool orphanPair = false;
        for (int t = 0; t < 34; t++)
        {
            if (c[t] == 0) continue;
            kinds++;
            if (c[t] >= 2) pairs++;
            if (IsTermOrHonor(t))
            {
                orphanKinds++;
                if (c[t] >= 2) orphanPair = true;
            }
        }
        int chiitoi = 6 - pairs + (kinds < 7 ? 7 - kinds : 0);
        int kokushi = 13 - orphanKinds - (orphanPair ? 1 : 0);
        if (chiitoi < best) best = chiitoi;
        if (kokushi < best) best = kokushi;
        return best;
    }

    /// Combines the group tables; group swapGroup (if >= 0) is read from spareTable instead.
    int StandardShanten(int meldCount, int swapGroup)
    {
        for (int i = 0; i < 10; i++) dpA[i] = -1;
        dpA[0] = 0;
        for (int g = 0; g < 4; g++)
        {
            int[] tab = g == swapGroup ? spareTable : groupTable;
            int off = g == swapGroup ? 0 : g * 10;
            for (int i = 0; i < 10; i++) dpB[i] = -1;
            for (int p1 = 0; p1 < 2; p1++)
                for (int m1 = 0; m1 < 5; m1++)
                {
                    int a = dpA[p1 * 5 + m1];
                    if (a < 0) continue;
                    for (int p2 = 0; p2 + p1 < 2; p2++)
                        for (int m2 = 0; m1 + m2 < 5; m2++)
                        {
                            int b = tab[off + p2 * 5 + m2];
                            if (b < 0) continue;
                            int k = (p1 + p2) * 5 + m1 + m2;
                            if (a + b > dpB[k]) dpB[k] = a + b;
                        }
                }
            for (int i = 0; i < 10; i++) dpA[i] = dpB[i];
        }
        int best = 8;
        for (int p = 0; p < 2; p++)
            for (int m = 0; m < 5; m++)
            {
                int partials = dpA[p * 5 + m];
                if (partials < 0) continue;
                int sets = m + meldCount;
                if (sets > 4) continue;
                if (partials > 4 - sets) partials = 4 - sets;
                int s = 8 - 2 * sets - partials - p;
                if (s < best) best = s;
            }
        return best;
    }

    void BuildGroup(int[] c, int g, int[] table, int offset)
    {
        scanHonor = g == 3;
        scanLen = scanHonor ? 7 : 9;
        // The three suits behave identically, so results are cached by tile counts (honors separately).
        int key = scanHonor ? 1 : 2;
        for (int i = 0; i < 9; i++)
        {
            gc[i] = i < scanLen ? c[g * 9 + i] : 0;
            key = key * 5 + gc[i];
        }
        int slot = key % CacheSize;
        if (cacheKeys[slot] == key)
        {
            for (int i = 0; i < 10; i++) table[offset + i] = cacheTabs[slot * 10 + i];
            return;
        }
        stamp++;
        int k = Solve(0, gc[0], gc[1]);
        cacheKeys[slot] = key;
        for (int i = 0; i < 10; i++)
        {
            table[offset + i] = memoTab[k * 10 + i];
            cacheTabs[slot * 10 + i] = memoTab[k * 10 + i];
        }
    }

    /// Best partial-set counts for tiles i.. of the group, where position i has a tiles left and i + 1 has b
    /// (later positions are untouched). Each way of splitting position i's tiles into sequences, partial
    /// sequences, a triplet, the pair, pair-shaped partials and leftovers is tried once. Returns the memo slot.
    [RecursiveMethod]
    int Solve(int i, int a, int b)
    {
        if (i >= scanLen)
        {
            if (memoStamp[225] != stamp)
            {
                for (int j = 0; j < 10; j++) memoTab[2250 + j] = -1;
                memoTab[2250] = 0;
                memoStamp[225] = stamp;
            }
            return 225;
        }
        int k = (i * 5 + a) * 5 + b;
        if (memoStamp[k] == stamp) return k;
        for (int j = 0; j < 10; j++) memoTab[k * 10 + j] = -1;

        int c2 = i + 2 < scanLen ? gc[i + 2] : 0;
        bool seq = !scanHonor;
        int maxX = 0;
        if (seq && i <= 6) { maxX = a < b ? a : b; if (c2 < maxX) maxX = c2; }
        for (int x = 0; x <= maxX; x++)
        {
            int maxY = seq && i <= 7 ? (a < b ? a : b) - x : 0;
            for (int y = 0; y <= maxY; y++)
            {
                int maxZ = 0;
                if (seq && i <= 6) maxZ = (a - x - y < c2 - x) ? a - x - y : c2 - x;
                for (int z = 0; z <= maxZ; z++)
                {
                    int child = Solve(i + 1, b - x - y, c2 - x - z);
                    int r = a - x - y - z;
                    for (int pon = 0; pon * 3 <= r; pon++)
                        for (int head = 0; head <= 1 && pon * 3 + head * 2 <= r; head++)
                            for (int pairs = 0; pon * 3 + head * 2 + pairs * 2 <= r; pairs++)
                                Merge(k, child, head, x + pon, y + z + pairs);
                }
            }
        }
        memoStamp[k] = stamp;
        return k;
    }

    void Merge(int k, int child, int head, int addSets, int addPartials)
    {
        for (int pc = 0; pc + head < 2; pc++)
        {
            for (int mc = 0; mc < 5; mc++)
            {
                int v = memoTab[child * 10 + pc * 5 + mc];
                if (v < 0) continue;
                int m = mc + addSets;
                if (m > 4) m = 4;
                int t = v + addPartials;
                if (t > 4) t = 4;
                int idx = k * 10 + (pc + head) * 5 + m;
                if (t > memoTab[idx]) memoTab[idx] = t;
            }
        }
    }

    // ================= helpers =================

    public int BasePoints(int han, int fu)
    {
        if (han >= 13) return 8000;
        if (han >= 11) return 6000;
        if (han >= 8) return 4000;
        if (han >= 6) return 3000;
        if (han >= 5) return 2000;
        int b = fu;
        for (int i = 0; i < han + 2; i++) b *= 2;
        return b > 2000 ? 2000 : b;
    }

    public int RoundUp100(int v)
    {
        return ((v + 99) / 100) * 100;
    }

    public string LimitName(int basePts, int yakuman)
    {
        if (yakuman > 1) return yakuman + "x Yakuman";
        if (basePts >= 8000) return "Yakuman";
        if (basePts >= 6000) return "Sanbaiman";
        if (basePts >= 4000) return "Baiman";
        if (basePts >= 3000) return "Haneman";
        if (basePts >= 2000) return "Mangan";
        return "";
    }

    public int DoraFromIndicator(int t)
    {
        if (t < 27) return (t % 9 == 8) ? t - 8 : t + 1;
        if (t <= 30) return t == 30 ? 27 : t + 1;
        return t == 33 ? 31 : t + 1;
    }

    public bool IsTermOrHonor(int t)
    {
        return t >= 27 || t % 9 == 0 || t % 9 == 8;
    }

    public bool IsAka(int id)
    {
        return id == 16 || id == 52 || id == 88;
    }

    string Add(string text, string yaku, int han)
    {
        string item = han >= 13 ? yaku : yaku + " " + han;
        return text.Length == 0 ? item : text + ", " + item;
    }
}
