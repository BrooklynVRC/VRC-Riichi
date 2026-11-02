#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TMPro;
using UdonSharpEditor;

/// Menu: Tools > Mahjong > Build Table (4 players, at the world origin)
///       Tools > Mahjong > Build 3-Player Table (sanma, beside it)
/// Builds a complete, wired-up riichi mahjong table.
public static class MahjongTableBuilder
{

    const float TableY = 0.775f; // top surface height
    // Layout scale: tiles, spacing and the table all grow together, so nothing overlaps.
    const float S = 1.25f;
    static readonly Vector3 TileSize = new Vector3(0.05f, 0.07f, 0.035f) * S;
    const float Pitch = 0.054f * S;   // tile spacing
    const float RowPitch = 0.076f * S; // spacing between rows of flat tiles
    const float TableHalf = 1.2f * S;
    const string MaterialFolder = "Assets/Mahjong/Materials";
    const string AtlasPath = "Assets/Mahjong/Textures/MahjongTileAtlas.png";

    static Material tableMat, tileMat, tileBackMat, tileFaceMat, buttonMat, sitButtonMat;
    static List<Renderer> buttonRenderers, sitButtonRenderers;

    [MenuItem("Tools/Mahjong/Build Table")]
    public static void Build()
    {
        BuildTable(false);
    }

    /// Same table with the fourth seat left out, threePlayer on, and a row for set-aside Norths (kita) per seat.
    [MenuItem("Tools/Mahjong/Build 3-Player Table")]
    public static void Build3Player()
    {
        BuildTable(true);
    }

    static void BuildTable(bool threePlayer)
    {
        var root = new GameObject(threePlayer ? "MahjongTable3P" : "MahjongTable");
        // The 3-player table goes beside the 4-player one so both fit in the same world.
        if (threePlayer) root.transform.position = new Vector3(TableHalf * 2f + 1.5f, 0, 0);
        Undo.RegisterCreatedObjectUndo(root, "Build Mahjong Table");
        var game = UdonSharpUndo.AddComponent<MahjongGame>(root);
        var rules = UdonSharpUndo.AddComponent<MahjongRules>(root);

        // Shared material assets: edit one and everything using it changes.
        tableMat = GetOrCreateMaterial("MJ_Table", new Color(0.05f, 0.3f, 0.2f));
        tileMat = GetOrCreateMaterial("MJ_Tile", new Color(0.96f, 0.94f, 0.86f));
        tileBackMat = GetOrCreateMaterial("MJ_TileBack", new Color(0.1f, 0.45f, 0.3f));
        tileFaceMat = GetOrCreateFaceMaterial();
        EnableEmission(tileMat);
        EnableEmission(tileFaceMat);
        buttonMat = GetOrCreateMaterial("MJ_Button", new Color(0.85f, 0.85f, 0.85f));
        sitButtonMat = GetOrCreateMaterial("MJ_SitButton", new Color(0.95f, 0.75f, 0.2f));
        buttonRenderers = new List<Renderer>();
        sitButtonRenderers = new List<Renderer>();

        // Table
        var top = Prim(PrimitiveType.Cube, "TableTop", root.transform, new Vector3(0, 0.75f, 0), Quaternion.identity, new Vector3(TableHalf * 2f, 0.05f, TableHalf * 2f));
        var leg = Prim(PrimitiveType.Cylinder, "Leg", root.transform, new Vector3(0, 0.36f, 0), Quaternion.identity, new Vector3(0.3f, 0.36f, 0.3f));
        var tableRenderers = new[] { top.GetComponent<Renderer>(), leg.GetComponent<Renderer>() };
        foreach (var r in tableRenderers) r.sharedMaterial = tableMat;

        // Status board (billboards toward the local player)
        var statusAnchor = new GameObject("StatusAnchor").transform;
        statusAnchor.SetParent(root.transform, false);
        statusAnchor.localPosition = new Vector3(0, 2.2f, 0);
        var status = Text("Status", statusAnchor, Vector3.zero, Quaternion.identity, 1.0f, new Vector2(2.0f, 0.9f), Color.white, true);

        // Announcer: the last discard shown large, plus big call text (PON! RIICHI! ...). Sits above the table
        // centre, high enough not to block the view across the table; billboards toward the local player.
        var announceAnchor = new GameObject("Announcer").transform;
        announceAnchor.SetParent(root.transform, false);
        announceAnchor.localPosition = new Vector3(0, 1.45f, 0);
        var announceTile = Tile("AnnounceTile", announceAnchor, new Vector3(0, 0.1f, 0), Quaternion.identity, game, 0, 0, true);
        announceTile.transform.localScale = Vector3.one * 2.5f;
        var announceCaption = Text("AnnounceCaption", announceAnchor, new Vector3(0, -0.04f, 0), Quaternion.identity, 0.6f, new Vector2(1.0f, 0.12f), Color.white, false);
        announceCaption.text = "";
        var announceText = Text("AnnounceText", announceAnchor, new Vector3(0, 0.34f, 0), Quaternion.identity, 2.4f, new Vector2(1.6f, 0.3f), Color.white, false);
        announceText.text = "";
        announceText.fontStyle = FontStyles.Bold;
        announceText.outlineWidth = 0.2f;
        announceText.outlineColor = new Color32(0, 0, 0, 255);

        // Winning hand at hand over: one row of tiles where the big discard tile sits, the yaku underneath.
        // The game spaces the tiles out (with gaps before the winning tile and each called set).
        const float WinScale = 1.3f;
        var winTiles = new MahjongTile[18];
        for (int k = 0; k < winTiles.Length; k++)
        {
            var wt = Tile("WinTile" + k, announceAnchor, new Vector3(0, 0.08f, 0), Quaternion.identity, game, 0, k, true);
            wt.transform.localScale = Vector3.one * WinScale;
            winTiles[k] = wt;
        }
        var winText = Text("WinText", announceAnchor, new Vector3(0, 0.08f - TileSize.y * WinScale * 0.5f - 0.06f, 0), Quaternion.identity, 0.55f, new Vector2(1.8f, 0.1f), new Color(1f, 0.9f, 0.6f), true);
        winText.text = "";
        winText.outlineWidth = 0.2f;
        winText.outlineColor = new Color32(0, 0, 0, 255);

        var seatLabels = new TMP_Text[4];
        var actionButtons = new MahjongButton[4 * MahjongGame.ButtonSlots];
        var pondAnchors = new Transform[4];
        var meldAnchors = new Transform[4];
        var handTiles = new MahjongTile[56];
        var discardTiles = new MahjongTile[96];
        var meldTiles = new MahjongTile[64];
        var waitTiles = new MahjongTile[4 * MahjongGame.WaitSlots];
        var waitLabels = new TMP_Text[4];
        var kitaTiles = new MahjongTile[16];
        var deltaLabels = new TMP_Text[4];
        var windMarkers = new TMP_Text[4];
        var riichiSticks = new GameObject[4];
        var flat = Quaternion.Euler(90, 0, 0);
        float flatY = TableY + TileSize.z * 0.5f;

        // 3-player: seat 4 (the North side) is left empty; its array entries stay null.
        for (int s = 0; s < (threePlayer ? 3 : 4); s++)
        {
            // Each seat is built in local space: player at -Z looking toward +Z. Seats go counter-clockwise.
            var anchor = new GameObject("Seat" + (s + 1)).transform;
            anchor.SetParent(root.transform, false);
            anchor.localRotation = Quaternion.Euler(0, -90 * s, 0);

            var seatGo = ButtonObject("SitButton", anchor, new Vector3(0.58f * S, TableY + 0.012f, -1.1f * S), sitButtonMat, sitButtonRenderers, "Sit");
            var seat = UdonSharpUndo.AddComponent<MahjongSeat>(seatGo);
            seat.game = game; seat.seatIndex = s;
            EditorUtility.SetDirty(seat);

            var botGo = ButtonObject("BotButton", anchor, new Vector3(0.68f * S, TableY + 0.012f, -1.1f * S), buttonMat, buttonRenderers, "Bot");
            var bot = UdonSharpUndo.AddComponent<MahjongSeat>(botGo);
            bot.game = game; bot.seatIndex = s; bot.botButton = true;
            EditorUtility.SetDirty(bot);

            // Name tag above and behind the player, facing the table: readable by the others, out of the player's own view.
            seatLabels[s] = Text("SeatLabel", anchor, new Vector3(0, TableY + 1.3f, -1.75f * S), Quaternion.Euler(0, 180, 0), 1.0f, new Vector2(1.4f, 0.35f), Color.white, false);

            // Points won or lost this hand, floating over this side of the table (billboards toward the viewer).
            // Point sticks fly to and from the spot under it.
            deltaLabels[s] = Text("PointsDelta", anchor, new Vector3(0, TableY + 0.3f, -0.72f * S), Quaternion.identity, 1.2f, new Vector2(0.6f, 0.15f), Color.white, false);
            deltaLabels[s].text = "";
            deltaLabels[s].fontStyle = FontStyles.Bold;
            deltaLabels[s].outlineWidth = 0.2f;
            deltaLabels[s].outlineColor = new Color32(0, 0, 0, 255);

            // Seat wind marker: a plaque on the table left of the hand, with the wind standing over it
            // (billboards toward the viewer; gold with DEALER for the dealer).
            var plaque = Prim(PrimitiveType.Cube, "WindPlaque", anchor, new Vector3(-0.6f * S, TableY + 0.004f, -0.8f * S), Quaternion.identity, new Vector3(0.09f, 0.008f, 0.09f) * S);
            Object.DestroyImmediate(plaque.GetComponent<Collider>());
            plaque.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("MJ_WindMarker", new Color(0.12f, 0.12f, 0.14f));
            windMarkers[s] = Text("WindMarker", anchor, new Vector3(-0.6f * S, TableY + 0.1f, -0.8f * S), Quaternion.identity, 0.7f, new Vector2(0.4f, 0.14f), Color.white, false);
            windMarkers[s].text = "";
            windMarkers[s].fontStyle = FontStyles.Bold;
            windMarkers[s].outlineWidth = 0.2f;
            windMarkers[s].outlineColor = new Color32(0, 0, 0, 255);

            // Riichi stick: laid across the table just inside the pond while this seat is in riichi.
            var rs = Prim(PrimitiveType.Cube, "RiichiStick", anchor, new Vector3(0, TableY + 0.005f, -0.33f * S + TileSize.y * 0.5f + 0.02f), Quaternion.Euler(0, 90, 0), new Vector3(0.018f, 0.008f, 0.1f) * S * 1.4f);
            Object.DestroyImmediate(rs.GetComponent<Collider>());
            rs.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("MJ_PointStick", new Color(0.97f, 0.97f, 0.95f));
            var rsDot = Prim(PrimitiveType.Cube, "Dot", rs.transform, new Vector3(0, 0.55f, 0), Quaternion.identity, new Vector3(0.6f, 0.2f, 0.12f));
            Object.DestroyImmediate(rsDot.GetComponent<Collider>());
            rsDot.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("MJ_PointStickDot", new Color(0.8f, 0.1f, 0.1f));
            rs.SetActive(false);
            riichiSticks[s] = rs;

            // Hand: 13 tiles plus the drawn tile set slightly apart
            for (int k = 0; k < 14; k++)
            {
                float x = -0.38f * S + k * Pitch + (k == 13 ? 0.025f * S : 0f);
                var pos = new Vector3(x, TableY + TileSize.y * 0.5f, -0.95f * S);
                handTiles[s * 14 + k] = Tile("Hand" + k, anchor, pos, Quaternion.Euler(15, 0, 0), game, s, k, false);
            }

            // Waits: small tiles floating above the hand, tilted like it. Only the seat's own player sees them.
            var waits = new GameObject("Waits").transform;
            waits.SetParent(anchor, false);
            waits.localPosition = new Vector3(0, TableY + TileSize.y + 0.07f * S, -0.97f * S);
            waits.localRotation = Quaternion.Euler(15, 0, 0);
            const float WaitScale = 0.6f;
            for (int k = 0; k < MahjongGame.WaitSlots; k++)
            {
                var pos = new Vector3((k - (MahjongGame.WaitSlots - 1) * 0.5f) * Pitch * WaitScale * 1.05f, 0f, 0f);
                var wt = Tile("Wait" + k, waits, pos, Quaternion.identity, game, s, k, true);
                wt.transform.localScale = Vector3.one * WaitScale;
                waitTiles[s * MahjongGame.WaitSlots + k] = wt;
            }
            waitLabels[s] = Text("WaitLabel", waits, new Vector3(0, TileSize.y * WaitScale * 0.5f + 0.018f, 0), Quaternion.identity, 0.3f * S, new Vector2(0.7f, 0.05f), Color.white, false);
            waitLabels[s].text = "";

            // Discards: 6 per row toward the center, grouped under a pond anchor at the pond's centre so each
            // client can turn and tilt the other players' ponds toward itself.
            float pondZ = -0.33f * S - 1.5f * RowPitch;
            var pond = new GameObject("Pond").transform;
            pond.SetParent(anchor, false);
            pond.localPosition = new Vector3(0, flatY, pondZ);
            pondAnchors[s] = pond;
            for (int k = 0; k < 24; k++)
            {
                var pos = new Vector3(-2.5f * Pitch + (k % 6) * Pitch, 0f, -0.33f * S - (k / 6) * RowPitch - pondZ);
                discardTiles[s * 24 + k] = Tile("Discard" + k, pond, pos, flat, game, s, k, true);
            }

            // Melds: two melds per row on the player's right, grouped like the pond so they can face viewers.
            float meldX = 0.47f * S + 3.5f * Pitch;
            float meldZ = -0.78f * S - 0.5f * (RowPitch + 0.002f);
            var meldGroup = new GameObject("Melds").transform;
            meldGroup.SetParent(anchor, false);
            meldGroup.localPosition = new Vector3(meldX, flatY, meldZ);
            meldAnchors[s] = meldGroup;
            for (int k = 0; k < 16; k++)
            {
                var pos = new Vector3(0.47f * S + (k % 8) * Pitch - meldX, 0f, -0.78f * S - (k / 8) * (RowPitch + 0.002f) - meldZ);
                meldTiles[s * 16 + k] = Tile("Meld" + k, meldGroup, pos, flat, game, s, k, true);
            }

            // Kita (3-player): set-aside Norths in a row just left of the melds, turning with them.
            if (threePlayer)
            {
                for (int k = 0; k < 4; k++)
                {
                    var pos = new Vector3(0.47f * S - 0.02f - (4 - k) * Pitch - meldX, 0f, -0.78f * S - meldZ);
                    kitaTiles[s * 4 + k] = Tile("Kita" + k, meldGroup, pos, flat, game, s, k, true);
                }
            }

            // Context buttons: the game fills them left to right with only the choices available right now.
            for (int b = 0; b < MahjongGame.ButtonSlots; b++)
            {
                var pos = new Vector3(-0.42f + b * 0.12f, TableY + 0.012f, -1.1f * S);
                var go = ButtonObject("Action" + b, anchor, pos, buttonMat, buttonRenderers, "", 0.11f);
                var btn = UdonSharpUndo.AddComponent<MahjongButton>(go);
                btn.game = game; btn.seat = s; btn.slot = b;
                btn.label = go.GetComponentInChildren<TextMeshPro>();
                btn.face = buttonRenderers[buttonRenderers.Count - 1];
                actionButtons[s * MahjongGame.ButtonSlots + b] = btn;
                EditorUtility.SetDirty(btn);
            }
        }

        // Dora indicators in the middle of the table
        var doraTiles = new MahjongTile[5];
        // Grouped under one anchor so each client can turn and tilt them toward itself, like the ponds.
        var doraAnchor = new GameObject("DoraIndicators").transform;
        doraAnchor.SetParent(root.transform, false);
        doraAnchor.localPosition = new Vector3(0, flatY, 0);
        for (int d = 0; d < 5; d++)
            doraTiles[d] = Tile("Dora" + d, doraAnchor, new Vector3(-2 * Pitch + d * Pitch, 0f, 0f), flat, game, 0, d, true);
        Text("DoraLabel", doraAnchor, new Vector3(0, TableY + 0.002f - flatY, -0.08f * S), flat, 0.35f * S, new Vector2(0.4f, 0.06f), Color.white, false).text = "DORA";

        // Sounds: one 3D source over the table centre. Drop clips into the Sounds slots on MahjongGame.
        var sfxGo = new GameObject("Sounds");
        sfxGo.transform.SetParent(root.transform, false);
        sfxGo.transform.localPosition = new Vector3(0, TableY + 0.2f, 0);
        var sfx = sfxGo.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 1f;
        sfx.minDistance = 2f;
        sfx.maxDistance = 12f;
        sfx.rolloffMode = AudioRolloffMode.Linear;

        // Point sticks for the end-of-hand animation: hidden until the game flies them between seats.
        var stickMat = GetOrCreateMaterial("MJ_PointStick", new Color(0.97f, 0.97f, 0.95f));
        var sticksGroup = new GameObject("PointSticks").transform;
        sticksGroup.SetParent(root.transform, false);
        var pointSticks = new Transform[24];
        for (int k = 0; k < pointSticks.Length; k++)
        {
            var st = Prim(PrimitiveType.Cube, "Stick" + k, sticksGroup, new Vector3(0, TableY + 0.05f, 0), Quaternion.identity, new Vector3(0.018f, 0.008f, 0.1f) * S);
            Object.DestroyImmediate(st.GetComponent<Collider>());
            st.GetComponent<Renderer>().sharedMaterial = stickMat;
            var dot = Prim(PrimitiveType.Cube, "Dot", st.transform, new Vector3(0, 0.55f, 0), Quaternion.identity, new Vector3(0.6f, 0.2f, 0.12f));
            Object.DestroyImmediate(dot.GetComponent<Collider>());
            dot.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("MJ_PointStickDot", new Color(0.8f, 0.1f, 0.1f));
            st.SetActive(false);
            pointSticks[k] = st.transform;
        }

        game.rules = rules;
        game.threePlayer = threePlayer;
        if (threePlayer)
        {
            game.kitaTiles = kitaTiles;
            game.startingPoints = 35000;
        }
        game.waitTiles = waitTiles;
        game.waitLabels = waitLabels;
        game.sfxSource = sfx;
        game.statusText = status;
        game.statusAnchor = statusAnchor;
        game.seatLabels = seatLabels;
        game.handTiles = handTiles;
        game.actionButtons = actionButtons;
        game.pondAnchors = pondAnchors;
        game.doraAnchor = doraAnchor;
        game.meldAnchors = meldAnchors;
        game.discardTiles = discardTiles;
        game.meldTiles = meldTiles;
        game.doraTiles = doraTiles;
        game.announceAnchor = announceAnchor;
        game.announceTile = announceTile;
        game.announceText = announceText;
        game.announceCaption = announceCaption;
        game.winTiles = winTiles;
        game.winText = winText;
        game.winTilePitch = Pitch * WinScale * 1.04f;
        game.pointSticks = pointSticks;
        game.deltaLabels = deltaLabels;
        game.windMarkers = windMarkers;
        game.riichiStickMarkers = riichiSticks;
        game.tableRenderers = tableRenderers;
        game.buttonRenderers = buttonRenderers.ToArray();
        game.sitButtonRenderers = sitButtonRenderers.ToArray();
        game.tableMaterial = tableMat;
        game.tileMaterial = tileMat;
        game.tileBackMaterial = tileBackMat;
        game.tileFaceMaterial = tileFaceMat;
        game.buttonMaterial = buttonMat;
        game.sitButtonMaterial = sitButtonMat;
        EditorUtility.SetDirty(game);

        Selection.activeGameObject = root;
        Debug.Log("[Mahjong] Table built. Tick 'Debug Control All Seats' on MahjongGame to test solo. Materials are in " + MaterialFolder + ".");
    }

    /// Menu: Tools > Mahjong > Apply Materials
    /// Pushes each MahjongGame's material slots onto its tiles, buttons and table in edit mode
    /// (the game also does this itself when the world starts).
    [MenuItem("Tools/Mahjong/Apply Materials")]
    public static void ApplyMaterials()
    {
        int count = 0;
        foreach (var game in Object.FindObjectsOfType<MahjongGame>())
        {
            ApplyTiles(game.handTiles, game);
            ApplyTiles(game.discardTiles, game);
            ApplyTiles(game.meldTiles, game);
            ApplyTiles(game.doraTiles, game);
            ApplyTiles(game.waitTiles, game);
            ApplyTiles(game.kitaTiles, game);
            ApplyTiles(game.winTiles, game);
            ApplyRenderers(game.tableRenderers, game.tableMaterial);
            ApplyRenderers(game.buttonRenderers, game.buttonMaterial);
            ApplyRenderers(game.sitButtonRenderers, game.sitButtonMaterial);
            count++;
        }
        Debug.Log("[Mahjong] Applied materials to " + count + " table(s).");
    }

    /// Menu: Tools > Mahjong > Save Selected Table As Prefab
    [MenuItem("Tools/Mahjong/Save Selected Table As Prefab")]
    public static void SaveAsPrefab()
    {
        var go = Selection.activeGameObject;
        if (go == null || go.GetComponent<MahjongGame>() == null)
        {
            EditorUtility.DisplayDialog("Mahjong", "Select the MahjongTable object in the scene first.", "OK");
            return;
        }
        string path = EditorUtility.SaveFilePanelInProject("Save Mahjong Table Prefab", go.name, "prefab", "Where to save the table prefab", "Assets/Mahjong");
        if (string.IsNullOrEmpty(path)) return;
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);
        if (prefab != null)
        {
            EditorGUIUtility.PingObject(prefab);
            Debug.Log("[Mahjong] Saved " + path + ". To share it, right-click the Mahjong folder > Export Package.");
        }
    }

    static void ApplyTiles(MahjongTile[] tiles, MahjongGame game)
    {
        if (tiles == null) return;
        foreach (var tile in tiles)
        {
            if (tile == null) continue;
            Undo.RecordObject(tile, "Apply Mahjong Materials");
            if (game.tileMaterial != null) tile.bodyMaterial = game.tileMaterial;
            tile.backMaterial = game.tileBackMaterial;
            if (game.tileFaceMaterial != null) tile.faceArtMaterial = game.tileFaceMaterial;
            EditorUtility.SetDirty(tile);
            if (tile.body != null && game.tileMaterial != null)
            {
                Undo.RecordObject(tile.body, "Apply Mahjong Materials");
                tile.body.sharedMaterial = game.tileMaterial;
            }
            if (tile.face != null && game.tileFaceMaterial != null)
            {
                Undo.RecordObject(tile.face, "Apply Mahjong Materials");
                tile.face.sharedMaterial = game.tileFaceMaterial;
            }
        }
    }

    static void ApplyRenderers(Renderer[] renderers, Material m)
    {
        if (renderers == null || m == null) return;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            Undo.RecordObject(r, "Apply Mahjong Materials");
            r.sharedMaterial = m;
        }
    }

    /// Face material showing MahjongTileAtlas.png; each tile selects its cell with a UV offset at runtime.
    static Material GetOrCreateFaceMaterial()
    {
        var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
        if (importer != null)
        {
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
        var mat = GetOrCreateMaterial("MJ_TileFace", Color.white);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        if (mat.mainTexture == null && tex != null)
        {
            mat.mainTexture = tex;
            mat.SetFloat("_Glossiness", 0.35f);
            EditorUtility.SetDirty(mat);
        }
        if (tex == null) Debug.LogWarning("[Mahjong] " + AtlasPath + " not found; tiles will show blank faces.");
        return mat;
    }

    /// Turns on emission (black, so it looks the same) so the game can make dora tiles glow at runtime.
    static void EnableEmission(Material mat)
    {
        if (mat == null || mat.IsKeywordEnabled("_EMISSION")) return;
        mat.EnableKeyword("_EMISSION");
        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
    }

    static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Mahjong", "Materials");
        var mat = new Material(Shader.Find("Standard")) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ---------------- helpers ----------------
    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        return go;
    }

    static TextMeshPro Text(string name, Transform parent, Vector3 pos, Quaternion rot, float size, Vector2 rect, Color color, bool wrap)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        var t = go.AddComponent<TextMeshPro>();
        t.fontSize = size;
        t.rectTransform.sizeDelta = rect;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = wrap;
        t.richText = true;
        t.color = color;
        t.text = name;
        return t;
    }

    static GameObject ButtonObject(string name, Transform parent, Vector3 pos, Material material, List<Renderer> renderers, string label, float width = 0.08f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(width, 0.02f, 0.045f);

        var face = Prim(PrimitiveType.Cube, "Face", go.transform, Vector3.zero, Quaternion.identity, new Vector3(width, 0.02f, 0.045f));
        Object.DestroyImmediate(face.GetComponent<Collider>());
        var faceRenderer = face.GetComponent<Renderer>();
        faceRenderer.sharedMaterial = material;
        renderers.Add(faceRenderer);

        var t = Text("Label", go.transform, new Vector3(0, 0.0115f, 0), Quaternion.Euler(90, 0, 0), 0.15f, new Vector2(width - 0.006f, 0.04f), Color.black, false);
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.06f;
        t.fontSizeMax = 0.15f;
        t.text = label;
        return go;
    }

    static MahjongTile Tile(string name, Transform parent, Vector3 pos, Quaternion rot, MahjongGame game, int seat, int slot, bool displayOnly)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        if (!displayOnly)
        {
            var col = go.AddComponent<BoxCollider>();
            col.size = TileSize;
        }

        var body = Prim(PrimitiveType.Cube, "Body", go.transform, Vector3.zero, Quaternion.identity, TileSize);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.GetComponent<Renderer>().sharedMaterial = tileMat;

        var label = Text("Label", go.transform, new Vector3(0, 0, -TileSize.z * 0.5f - 0.0006f), Quaternion.identity, 0.28f * S, new Vector2(TileSize.x, TileSize.y), Color.black, false);
        label.text = "";

        // Picture quad on the front face (a Quad's visible side faces -Z, toward the tile's owner).
        var face = Prim(PrimitiveType.Quad, "Face", go.transform, new Vector3(0, 0, -TileSize.z * 0.5f - 0.0005f), Quaternion.identity, new Vector3(TileSize.x * 0.94f, TileSize.y * 0.94f, 1f));
        Object.DestroyImmediate(face.GetComponent<Collider>());
        var faceRenderer = face.GetComponent<Renderer>();
        faceRenderer.sharedMaterial = tileFaceMat;
        faceRenderer.enabled = false; // the game turns it on with the right atlas cell

        // Number badge in the top-left corner of the picture, just in front of it.
        var number = Text("Number", go.transform, new Vector3(-TileSize.x * 0.2f, TileSize.y * 0.33f, -TileSize.z * 0.5f - 0.001f), Quaternion.identity, 0.2f * S, new Vector2(TileSize.x * 0.5f, TileSize.y * 0.3f), Color.black, false);
        number.alignment = TextAlignmentOptions.TopLeft;
        number.fontStyle = FontStyles.Bold;
        number.text = "";
        number.enabled = false;

        var tile = UdonSharpUndo.AddComponent<MahjongTile>(go);
        tile.game = game; tile.seat = seat; tile.slot = slot; tile.displayOnly = displayOnly;
        tile.label = label;
        tile.body = body.GetComponent<Renderer>();
        tile.face = faceRenderer;
        tile.numberLabel = number;
        tile.bodyMaterial = tileMat;
        tile.backMaterial = tileBackMat;
        tile.faceArtMaterial = tileFaceMat;
        EditorUtility.SetDirty(tile);
        return tile;
    }
}
#endif
