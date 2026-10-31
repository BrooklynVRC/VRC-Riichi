using TMPro;
using UdonSharp;
using UnityEngine;

/// One tile visual on the table. Hand tiles are clickable; discards, melds and dora are display only.
/// Tile ids: see MahjongRules.
/// The face picture comes from a texture atlas (10 x 4 cells, same layout as MahjongTileAtlas.png) shown on a quad;
/// each tile picks its cell through the material's UV offset, so every tile shares one face material.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MahjongTile : UdonSharpBehaviour
{
    public MahjongGame game;
    public int seat;
    public int slot;
    public bool displayOnly;
    public TMP_Text label;      // text fallback when there is no face quad
    public Renderer body;
    public Renderer face;       // quad on the front of the tile showing the atlas picture
    public TMP_Text numberLabel; // small number / wind in the corner of the picture
    [Tooltip("Show the number (or wind) in the corner of the tile picture. The game's setting overrides this.")]
    public bool showNumber = true;

    [Header("Materials (the game's material slots override these)")]
    public Material bodyMaterial;
    [Tooltip("Optional. If empty, the body material is tinted green for tile backs.")]
    public Material backMaterial;
    public Material faceArtMaterial;

    // Atlas layout: 2048 x 1024 texture, cells 184 x 256 px, 10 columns x 4 rows from the top-left.
    const float CellU = 184f / 2048f;
    const float CellV = 0.25f;

    // Tints multiply over the material, so a plain white tint shows the material exactly as authored.
    Color faceColor = new Color(1f, 1f, 1f);
    Color highlightColor = new Color(1f, 0.85f, 0.4f);
    Color dimColor = new Color(0.6f, 0.6f, 0.58f);
    Color backColor = new Color(0.1f, 0.45f, 0.3f);
    Color callColor = new Color(0.45f, 1f, 0.45f);
    Color winColor = new Color(1f, 0.45f, 0.3f);
    Color selectColor = new Color(1f, 0.95f, 0.55f);

    const float LiftHeight = 0.012f;
    const float SelectLift = 0.03f;   // a selected tile rises clearly above the rest of the hand
    Vector3 basePos;
    bool hasBasePos = false;

    MaterialPropertyBlock bodyBlock;
    MaterialPropertyBlock faceBlock;

    /// True while this tile shows a dora face; the game pulses its glow with SetShine.
    [System.NonSerialized] public bool shining = false;
    Color shineNow = new Color(0f, 0f, 0f);

    public override void Interact()
    {
        if (displayOnly || game == null) return;
        game.OnTileClicked(seat, slot);
    }

    public void Hide()
    {
        SetVisible(false);
        if (face != null) face.enabled = false;
        if (numberLabel != null) numberLabel.enabled = false;
        StopShine();
    }

    /// tint: 0 normal, 1 selected / latest discard, 2 dimmed, 3 call / riichi / kan opportunity,
    /// 4 winning tile (ron / tsumo), 5 riichi declaration marker (gold, not lifted),
    /// 6 selected in your hand (raised high: click it again to discard)
    public void ShowFace(int id, int tint)
    {
        SetVisible(true);
        Color c = faceColor;
        if (tint == 1) c = highlightColor;
        else if (tint == 2) c = dimColor;
        else if (tint == 3) c = callColor;
        else if (tint == 4) c = winColor;
        else if (tint == 5) c = highlightColor;
        else if (tint == 6) c = selectColor;
        UseMaterial(body, bodyMaterial);
        bodyBlock = Tint(body, bodyBlock, c);

        if (face != null)
        {
            face.enabled = true;
            if (label != null) label.enabled = false;
            UseMaterial(face, faceArtMaterial);
            ShowAtlasCell(AtlasCell(id), c);
            if (numberLabel != null)
            {
                numberLabel.enabled = showNumber;
                numberLabel.text = CornerText(id);
            }
        }
        else if (label != null) label.text = FaceText(id);

        SetLifted(tint == 6 ? SelectLift : (tint == 1 || tint == 3 || tint == 4 ? LiftHeight : 0f));
        shining = game != null && game.IsDoraId(id);
        if (!shining) StopShine();
    }

    public void ShowBack()
    {
        SetVisible(true);
        if (face != null) face.enabled = false;
        if (numberLabel != null) numberLabel.enabled = false;
        if (label != null) label.text = "";
        if (backMaterial != null)
        {
            UseMaterial(body, backMaterial);
            bodyBlock = Tint(body, bodyBlock, faceColor);
        }
        else
        {
            UseMaterial(body, bodyMaterial);
            bodyBlock = Tint(body, bodyBlock, backColor);
        }
        SetLifted(0f);
        StopShine();
    }

    /// Sets the glow (emission) on the tile body and, more softly, on its picture.
    public void SetShine(Color c)
    {
        if (c.r == shineNow.r && c.g == shineNow.g && c.b == shineNow.b) return;
        shineNow = c;
        if (body != null)
        {
            if (bodyBlock == null) bodyBlock = new MaterialPropertyBlock();
            bodyBlock.SetColor("_EmissionColor", c);
            body.SetPropertyBlock(bodyBlock);
        }
        if (face != null && faceBlock != null)
        {
            faceBlock.SetColor("_EmissionColor", c * 0.5f);
            face.SetPropertyBlock(faceBlock);
        }
    }

    void StopShine()
    {
        shining = false;
        SetShine(new Color(0f, 0f, 0f));
    }

    /// Called by the game to apply its material slots.
    public void SetMaterials(Material bodyMat, Material backMat, Material faceMat)
    {
        if (bodyMat != null) bodyMaterial = bodyMat;
        backMaterial = backMat;
        if (faceMat != null) faceArtMaterial = faceMat;
    }

    void UseMaterial(Renderer r, Material m)
    {
        if (r != null && m != null && r.sharedMaterial != m) r.sharedMaterial = m;
    }

    /// Tints through a property block so every tile keeps sharing one material.
    MaterialPropertyBlock Tint(Renderer r, MaterialPropertyBlock block, Color c)
    {
        if (r == null) return block;
        if (block == null) block = new MaterialPropertyBlock();
        block.SetColor("_Color", c);
        block.SetColor("_BaseColor", c);
        r.SetPropertyBlock(block);
        return block;
    }

    void ShowAtlasCell(int cell, Color c)
    {
        if (faceBlock == null) faceBlock = new MaterialPropertyBlock();
        int col = cell % 10;
        int row = cell / 10;
        faceBlock.SetVector("_MainTex_ST", new Vector4(CellU, CellV, col * CellU, (3 - row) * CellV));
        faceBlock.SetColor("_Color", c);
        faceBlock.SetColor("_BaseColor", c);
        face.SetPropertyBlock(faceBlock);
    }

    /// Atlas cell (row * 10 + column) for a tile id. Rows: man, pin, sou, honors; red fives sit in column 5.
    int AtlasCell(int id)
    {
        int t = id / 4;
        if (t < 27)
        {
            int n = t % 9 + 1;
            bool red = id == 16 || id == 52 || id == 88;
            int col = red ? 5 : (n <= 5 ? n - 1 : n);
            return (t / 9) * 10 + col;
        }
        if (t <= 30) return 30 + (t - 27);   // E S W N
        if (t == 31) return 35;              // white dragon (blank)
        if (t == 32) return 36;              // green dragon
        return 34;                           // red dragon
    }

    /// Raises highlighted tiles so they stand out.
    void SetLifted(float height)
    {
        if (!hasBasePos)
        {
            basePos = transform.localPosition;
            hasBasePos = true;
        }
        transform.localPosition = basePos + new Vector3(0f, height, 0f);
    }

    void SetVisible(bool visible)
    {
        if (body != null) body.enabled = visible;
        if (label != null) label.enabled = visible && face == null;
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = visible && !displayOnly;
    }

    /// Corner badge: the number in its suit colour (red fives in red), or the wind / dragon letter.
    string CornerText(int id)
    {
        int t = id / 4;
        if (t < 27)
        {
            int n = t % 9 + 1;
            int suit = t / 9;
            string col = (id == 16 || id == 52 || id == 88) ? "#e01010" : (suit == 0 ? "#9a1010" : (suit == 1 ? "#10309a" : "#0d6a24"));
            string letter = suit == 0 ? "m" : (suit == 1 ? "p" : "s");
            return "<color=" + col + ">" + n + "<size=60%>" + letter + "</size></color>";
        }
        if (t == 27) return "E";
        if (t == 28) return "S";
        if (t == 29) return "W";
        if (t == 30) return "N";
        if (t == 31) return "<color=#5070b0>Wh</color>";
        if (t == 32) return "<color=#0d6a24>Gr</color>";
        return "<color=#e01010>Rd</color>";
    }

    string FaceText(int id)
    {
        int t = id / 4;
        if (t < 27)
        {
            int n = t % 9 + 1;
            bool red = id == 16 || id == 52 || id == 88;
            string num = red ? "<color=#d01010>" + n + "</color>" : n.ToString();
            int suit = t / 9;
            if (suit == 0) return num + "\n<size=60%><color=#b01818>man</color></size>";
            if (suit == 1) return num + "\n<size=60%><color=#1838b0>pin</color></size>";
            return num + "\n<size=60%><color=#127a2a>sou</color></size>";
        }
        if (t == 27) return "E";
        if (t == 28) return "S";
        if (t == 29) return "W";
        if (t == 30) return "N";
        if (t == 31) return "<size=70%><color=#6080c0>[  ]</color></size>";
        if (t == 32) return "<color=#127a2a>F</color>";
        return "<color=#d01010>C</color>";
    }
}
