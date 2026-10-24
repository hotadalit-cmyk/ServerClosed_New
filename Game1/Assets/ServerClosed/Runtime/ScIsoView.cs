using ServerClosed.Core;
// SERVERCLOSED — Runtime: изометрический рендер (порт GameCanvas.tsx, Unity-версия).
// Тайлы — один vertex-colored Mesh (туман войны и «пул света осязания» — цвета вершин),
// декорации — в том же меше, существа — пул квадов, слизень — динамический мягкий контур
// (порт SlimeJelly: пружины, дыхание, сплющивание, облизывание стен).
using System.Collections.Generic;
using UnityEngine;

namespace ServerClosed.Runtime
{
    public class ScIsoView : MonoBehaviour
    {
        [System.NonSerialized] public ScWorld world;
        public Camera cam;

        const float TW = 1.0f;   // ширина ромба
        const float TH = 0.5f;   // высота ромба

        Mesh _tileMesh;
        Mesh _slimeMesh;
        Material _mat;       // vertex-colored, transparent
        Material _slimeMat;
        Material _overlayMat;
        Mesh _overlay;

        struct Blob { public Mesh mesh; public MeshRenderer mr; public MeshFilter mf; }
        readonly List<Blob> _creatures = new List<Blob>();
        Blob _beacon;
        float _exploreTimer = 0;
        Vector3 _slimeCenter;
        Transform _slimeTransform;
        public Vector3 RenderedHeroPosition { get { return _slimeTransform != null ? _slimeTransform.position : Vector3.zero; } }
        Vector2[] _slimePts;    // текущие смещения точек контура
        Vector2[] _slimeVel;
        double _animT;

        // Мировые координаты тайла → Unity-плоскость
        public Vector3 TileToWorld(double tx, double ty)
        {
            return new Vector3((float)((tx - ty) * TW * 0.5), (float)(-(tx + ty) * TH * 0.5), 0);
        }

        public Vector3 PosToWorld(double x, double y) { return TileToWorld(x, y); }

        public Vector3 ScreenToTile(Vector3 screen)
        {
            Vector3 w = cam.ScreenToWorldPoint(screen);
            // обратное преобразование: sx = (tx-ty)*0.5 ; sy = -(tx+ty)*0.25
            double tx = (w.x / (TW * 0.5) + (-w.y) / (TH * 0.5)) * 0.5;
            double ty = (-w.y / (TH * 0.5) - w.x / (TW * 0.5)) * 0.5;
            return new Vector3((float)tx, (float)ty, 0);
        }

        void Start()
        {
            _mat = new Material(Resources.Load<Shader>("ScVertexColor"));
            _slimeMat = new Material(Resources.Load<Shader>("ScVertexColor"));
            _overlayMat = new Material(Resources.Load<Shader>("ScVertexColor"));
            _overlayMat.color = new Color(0, 0, 0, 0);
            _tileMesh = new Mesh { name = "sc_tiles" };
            _slimeMesh = new Mesh { name = "sc_slime" };
            _overlay = MakeQuadMesh();

            var go = new GameObject("sc_tiles_go");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = _tileMesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = _mat; mr.sortingOrder = 0;

            var sg = new GameObject("sc_slime_go");
            _slimeTransform = sg.transform;
            sg.transform.SetParent(transform, false);
            var smf = sg.AddComponent<MeshFilter>(); smf.sharedMesh = _slimeMesh;
            var smr = sg.AddComponent<MeshRenderer>(); smr.sharedMaterial = _slimeMat; smr.sortingOrder = 50;

            _beacon = MakeBlob(64, 950);
            _overlayMat.color = new Color(0, 0, 0, 0);
            var ov = new GameObject("sc_overlay");
            ov.transform.SetParent(transform, false);
            var omf = ov.AddComponent<MeshFilter>(); omf.sharedMesh = _overlay;
            var omr = ov.AddComponent<MeshRenderer>(); omr.sharedMaterial = _overlayMat; omr.sortingOrder = 900;
            ov.transform.localScale = new Vector3(300, 300, 1);
        }

        Blob MakeBlob(int verts, int order)
        {
            var b = new Blob();
            b.mesh = new Mesh();
            var go = new GameObject("sc_blob");
            go.transform.SetParent(transform, false);
            b.mf = go.AddComponent<MeshFilter>(); b.mf.sharedMesh = b.mesh;
            b.mr = go.AddComponent<MeshRenderer>(); b.mr.sharedMaterial = _mat; b.mr.sortingOrder = order;
            return b;
        }

        static Mesh MakeQuadMesh()
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();
            return m;
        }

        // ===== Цвета тайлов (порт seasonTerrainCol/fog) =====
        static Color TileColor(string t, float v, bool dungeon)
        {
            if (dungeon)
            {
                switch (t)
                {
                    case "rock": return v > 0.5 ? new Color(0.10f, 0.11f, 0.13f) : new Color(0.08f, 0.09f, 0.11f);
                    case "floor": return v > 0.5 ? new Color(0.16f, 0.15f, 0.12f) : new Color(0.14f, 0.13f, 0.11f);
                    case "glow": return new Color(0.14f, 0.18f, 0.17f);
                    case "puddle": return new Color(0.08f, 0.14f, 0.15f);
                }
                return new Color(0.09f, 0.09f, 0.09f);
            }
            switch (t)
            {
                case "moss": return v > 0.5 ? new Color(0.094f, 0.149f, 0.098f) : new Color(0.086f, 0.137f, 0.102f);
                case "glade": return v > 0.5 ? new Color(0.114f, 0.184f, 0.122f) : new Color(0.106f, 0.173f, 0.118f);
                case "path": return v > 0.5 ? new Color(0.169f, 0.141f, 0.098f) : new Color(0.161f, 0.133f, 0.09f);
                case "dirt": return v > 0.5 ? new Color(0.133f, 0.114f, 0.086f) : new Color(0.122f, 0.106f, 0.078f);
                case "ruin": return v > 0.5 ? new Color(0.137f, 0.125f, 0.133f) : new Color(0.125f, 0.118f, 0.125f);
                case "water": return new Color(0.071f, 0.165f, 0.161f);
                case "mountain": return v > 0.5 ? new Color(0.149f, 0.153f, 0.173f) : new Color(0.133f, 0.137f, 0.161f);
                case "marsh": return v > 0.5 ? new Color(0.102f, 0.137f, 0.09f) : new Color(0.09f, 0.125f, 0.082f);
                case "ash": return v > 0.5 ? new Color(0.141f, 0.141f, 0.149f) : new Color(0.129f, 0.129f, 0.137f);
            }
            return new Color(0.09f, 0.145f, 0.1f);
        }

        static Color DecoColor(string kind)
        {
            switch (kind)
            {
                case "tree": return new Color(0.10f, 0.20f, 0.11f);
                case "pine": return new Color(0.07f, 0.16f, 0.10f);
                case "bush": return new Color(0.12f, 0.21f, 0.12f);
                case "rock": return new Color(0.24f, 0.24f, 0.26f);
                case "mushroom": return new Color(0.45f, 0.25f, 0.22f);
                case "stone": return new Color(0.30f, 0.30f, 0.32f);
                case "hut": return new Color(0.30f, 0.22f, 0.14f);
                case "fence": return new Color(0.26f, 0.20f, 0.12f);
                case "campfire": return new Color(0.55f, 0.30f, 0.10f);
                case "well": return new Color(0.20f, 0.20f, 0.22f);
                case "crystal": return new Color(0.30f, 0.55f, 0.65f);
                case "bones": return new Color(0.55f, 0.53f, 0.45f);
                case "farm": return new Color(0.22f, 0.26f, 0.12f);
                case "reed": return new Color(0.20f, 0.28f, 0.14f);
                default: return new Color(0.2f, 0.2f, 0.2f);
            }
        }

        // ===== Перестройка меша тайлов+декора (по разведке) =====
        void RebuildTiles()
        {
            var d = world.data;
            bool dun = world.DungeonActive;
            int w = dun ? world.Dun.floors[world.Dun.depth - 1].w : d.mapJson.w;
            int h = dun ? world.Dun.floors[world.Dun.depth - 1].h : d.mapJson.h;
            string[] terr = dun ? world.Dun.floors[world.Dun.depth - 1].terrain : d.mapJson.terrain;

            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            var p = d.player;
            double touchR = world.TouchRadius() + 0.4;
            double visR = world.VisionRadius();

            for (int ty = 0; ty < h; ty++)
            {
                for (int tx = 0; tx < w; tx++)
                {
                    if (!world.IsTileExplored(tx, ty)) continue;
                    string t = terr[ty * w + tx];
                    float v = ((tx * 57 + ty * 131) % 100) / 100f;
                    var c = TileColor(t, v, dun);
                    var cx = TileToWorld(tx + 0.5, ty + 0.5);

                    // «пул света осязания»: тайлы вплотную к телу — полная яркость (порт visNow)
                    double dd = System.Math.Sqrt((tx + 0.5 - p.x) * (tx + 0.5 - p.x) + (ty + 0.5 - p.y) * (ty + 0.5 - p.y));
                    bool visNow = (visR > 0 && dd <= visR) || dd <= touchR;
                    if (!visNow) c *= 0.55f;
                    AddDiamond(verts, cols, tris, cx, TW * 0.5f, TH * 0.5f, c);
                }
            }

            // Декорации (только надземье)
            if (!dun)
            {
                foreach (var deco in d.mapJson.deco)
                {
                    int dtx = (int)deco.x, dty = (int)deco.y;
                    if (!world.IsTileExplored(Mathf.Clamp(dtx, 0, w - 1), Mathf.Clamp(dty, 0, h - 1))) continue;
                    var basePos = TileToWorld(deco.x, deco.y);
                    var c = DecoColor(deco.kind);
                    switch (deco.kind)
                    {
                        case "tree": case "pine":
                            AddDiamond(verts, cols, tris, basePos + new Vector3(0, 0.02f, 0), 0.34f, 0.17f, c * 0.7f);
                            AddDiamond(verts, cols, tris, basePos + new Vector3(0, 0.30f, 0), 0.22f, 0.11f, c);
                            break;
                        case "mushroom": AddDiamond(verts, cols, tris, basePos + new Vector3(0, 0.10f, 0), 0.12f, 0.06f, c); break;
                        case "crystal": AddDiamond(verts, cols, tris, basePos + new Vector3(0, 0.16f, 0), 0.10f, 0.22f, c); break;
                        case "stone": case "rock": AddDiamond(verts, cols, tris, basePos + new Vector3(0, 0.08f, 0), 0.16f, 0.09f, c); break;
                        case "hut": AddDiamond(verts, cols, tris, basePos + new Vector3(0, 0.22f, 0), 0.55f, 0.28f, c); break;
                        case "fence": AddDiamond(verts, cols, tris, basePos, 0.10f, 0.05f, c); break;
                        case "well": AddDiamond(verts, cols, tris, basePos, 0.2f, 0.1f, c); break;
                        case "campfire": AddDiamond(verts, cols, tris, basePos, 0.12f, 0.06f, c); break;
                        case "bones": AddDiamond(verts, cols, tris, basePos, 0.10f, 0.05f, c); break;
                        case "reed": AddDiamond(verts, cols, tris, basePos + new Vector3(0, 0.12f, 0), 0.06f, 0.14f, c); break;
                        default: AddDiamond(verts, cols, tris, basePos, 0.12f, 0.06f, c); break;
                    }
                }
            }

            _tileMesh.Clear();
            _tileMesh.SetVertices(verts);
            _tileMesh.SetColors(cols);
            _tileMesh.SetTriangles(tris, 0);
            _tileMesh.RecalculateBounds();
        }

        static void AddDiamond(List<Vector3> verts, List<Color> cols, List<int> tris, Vector3 center, float rx, float ry, Color c)
        {
            int b = verts.Count;
            verts.Add(center + new Vector3(0, ry, 0));
            verts.Add(center + new Vector3(rx, 0, 0));
            verts.Add(center + new Vector3(0, -ry, 0));
            verts.Add(center + new Vector3(-rx, 0, 0));
            cols.Add(c); cols.Add(c); cols.Add(c); cols.Add(c);
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }

        static void AddEllipse(List<Vector3> verts, List<Color> cols, List<int> tris, Vector3 center, float rx, float ry, Color c, int seg = 14)
        {
            int b = verts.Count;
            verts.Add(center); cols.Add(c);
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2;
                verts.Add(center + new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry, 0));
                cols.Add(c);
            }
            for (int i = 1; i <= seg; i++) { tris.Add(b); tris.Add(b + i); tris.Add(b + i + 1); }
        }

        // ===== Существа =====
        void UpdateCreatureBlobs()
        {
            var list = world.ActiveCreatures();
            while (_creatures.Count < list.Count) _creatures.Add(MakeBlob(20, 40));
            var p = world.data.player;
            var sn = world.data.senses;

            for (int i = 0; i < _creatures.Count; i++)
            {
                var b = _creatures[i];
                if (i >= list.Count) { b.mr.gameObject.SetActive(false); continue; }
                var c = list[i];
                bool touched = world.IsEntityTouchedNow(c.id);
                bool sensedNow = touched || world.VisionRadius() > 0 &&
                    System.Math.Sqrt((c.x - p.x) * (c.x - p.x) + (c.y - p.y) * (c.y - p.y)) <= world.VisionRadius();
                if (!world.IsEntitySensed(c.id)) { b.mr.gameObject.SetActive(false); continue; }
                b.mr.gameObject.SetActive(true);

                var pos = PosToWorld(c.x, c.y);
                b.mr.transform.position = pos;

                // цвет по виду
                Color col;
                switch (c.species)
                {
                    case "slug": col = new Color(0.35f, 0.55f, 0.30f); break;
                    case "beetle": col = new Color(0.42f, 0.33f, 0.16f); break;
                    case "firefly": col = new Color(0.85f, 0.80f, 0.35f); break;
                    case "rabbit": col = new Color(0.55f, 0.48f, 0.40f); break;
                    case "deer": col = new Color(0.50f, 0.40f, 0.28f); break;
                    case "wolf": col = new Color(0.32f, 0.33f, 0.36f); break;
                    case "toad": col = new Color(0.36f, 0.40f, 0.26f); break;
                    case "spider": col = new Color(0.30f, 0.26f, 0.30f); break;
                    case "spirit": col = new Color(0.70f, 0.62f, 0.85f); break;
                    case "crystalslug": col = new Color(0.40f, 0.66f, 0.78f); break;
                    case "mushling": col = new Color(0.55f, 0.38f, 0.34f); break;
                    case "guard": col = new Color(0.55f, 0.55f, 0.60f); break;
                    default: col = new Color(0.4f, 0.4f, 0.4f); break;
                }
                float size = 0.30f;
                if (touched || sensedNow)
                {
                    // коснулось телом — ясный контур; память — тусклее
                    if (!touched) col *= 0.55f;
                }
                else col *= 0.35f;

                if (c.dead)
                {
                    col *= 0.6f;
                    size = 0.34f;
                }
                float ry = c.dead ? size * 0.4f : size * 0.62f;

                var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
                AddEllipse(verts, cols, tris, Vector3.zero, size, ry, col);
                b.mesh.Clear();
                b.mesh.SetVertices(verts); b.mesh.SetColors(cols); b.mesh.SetTriangles(tris, 0);
                b.mesh.RecalculateBounds();
            }
        }

        // ===== Слизень: мягкий контур (порт SlimeJelly, ядро) =====
        const int SLIME_N = 18;
        void UpdateSlime(double dt)
        {
            var p = world.data.player;
            var target = PosToWorld(p.x, p.y) + new Vector3(0, 0.06f, 0);
            if (_slimePts == null)
            {
                _slimeCenter = target;
                _slimePts = new Vector2[SLIME_N];
                _slimeVel = new Vector2[SLIME_N];
                for (int i = 0; i < SLIME_N; i++) { _slimePts[i] = Vector2.zero; _slimeVel[i] = Vector2.zero; }
            }
            _slimeCenter = Vector3.Lerp(_slimeCenter, target, Mathf.Min(1, (float)dt * 10));
            _slimeTransform.position = _slimeCenter;

            float speed = new Vector2(p.vx, p.vy).magnitude;
            float k = Mathf.Min(1, speed / 2.4f);
            float breath = 1 + Mathf.Sin((float)_animT * 2.2f) * 0.025f * (1 - k);
            int organs = world.data.senses.unlocked.Length;
            float young = Mathf.Max(0, 4 - organs);
            float widthMul = 1 + young * 0.15f;
            float heightMul = Mathf.Max(0.52f, 1 - young * 0.16f);
            float R = 0.42f * (1 + Mathf.Min(0.5f, world.data.player.genome.installed.Count * 0.07f)) * breath;
            float rx = R * (1 + 0.22f * k) * widthMul;
            float ry = R * (1 - 0.15f * k) * heightMul * 0.74f;

            float wobA = (0.8f + 1.0f * k) * (0.85f + young * 0.18f);
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();

            // содержимое пищеварения — тёмные комки внутри (§5.3 E, §6.3)
            var dig = world.data.digestion;

            Vector3 core = Vector3.zero;
            verts.Add(core); cols.Add(new Color(0.91f, 1.0f, 0.86f, 0.55f));
            var rimPts = new Vector3[SLIME_N + 1];
            for (int i = 0; i < SLIME_N; i++)
            {
                int ii = i % SLIME_N;
                float a = (float)ii / SLIME_N * Mathf.PI * 2;
                var rest = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                var wob = new Vector2(Mathf.Sin((float)_animT * 5.2f + ii * 1.7f), Mathf.Cos((float)_animT * 4.6f + ii * 2.3f)) * 0.045f * wobA;
                var want = rest + wob;
                // пружина
                _slimeVel[ii] += (want - _slimePts[ii]) * Mathf.Min(1, (float)dt * 26f);
                _slimeVel[ii] *= 0.86f;
                _slimePts[ii] += _slimeVel[ii] * Mathf.Min(1, (float)dt * 26f);
                // облизывание стен: точка не выходит в непроходимое
                var wp = _slimeCenter + new Vector3(_slimePts[ii].x, _slimePts[ii].y, 0);
                var tile = ScreenToTileCam(wp);
                if (!world.IsWalkable(tile.x, tile.y))
                {
                    _slimePts[ii] = Vector2.Lerp(_slimePts[ii], rest * 0.4f, 0.5f);
                }
                rimPts[i] = new Vector3(_slimePts[ii].x, _slimePts[ii].y, 0);
            }
            rimPts[SLIME_N] = rimPts[0];
            // мембрана: внешняя полупрозрачная кромка + внутренний тон
            for (int i = 0; i < SLIME_N; i++)
            {
                verts.Add(rimPts[i]);
                cols.Add(new Color(0.24f, 0.54f, 0.30f, 0.50f));
                tris.Add(0); tris.Add(1 + i); tris.Add(1 + ((i + 1) % SLIME_N));
            }
            // внутренняя масса (меньше, светлее)
            int innerBase = verts.Count;
            verts.Add(Vector3.zero); cols.Add(new Color(0.75f, 0.95f, 0.66f, 0.35f));
            for (int i = 0; i <= SLIME_N; i++)
            {
                int ii = i % SLIME_N;
                verts.Add(rimPts[ii] * 0.62f);
                cols.Add(new Color(0.62f, 0.86f, 0.52f, 0.30f));
            }
            for (int i = 1; i <= SLIME_N; i++) { tris.Add(innerBase); tris.Add(innerBase + i); tris.Add(innerBase + i + 1); }

            // комки пищеварения сквозь мембрану
            foreach (var it in dig.Items())
            {
                float ph = (it.seed % 1000) / 1000f;
                var off = new Vector3(Mathf.Sin(ph * 9f + (float)_animT * 0.8f) * rx * 0.35f, Mathf.Cos(ph * 7f) * ry * 0.3f - ry * 0.15f, 0);
                Color cc;
                switch (it.kind)
                {
                    case 1: cc = new Color(0.42f, 0.50f, 0.60f, 0.85f); break;
                    case 2: cc = new Color(0.55f, 0.42f, 0.75f, 0.85f); break;
                    case 3: cc = new Color(0.42f, 0.48f, 0.26f, 0.85f); break;
                    default: cc = new Color(0.50f, 0.62f, 0.34f, 0.85f); break;
                }
                float shrink = 0.10f + 0.10f * (float)it.progress;
                AddEllipse(verts, cols, tris, off, shrink, shrink * 0.75f, cc, 10);
            }

            _slimeMesh.Clear();
            _slimeMesh.SetVertices(verts); _slimeMesh.SetColors(cols); _slimeMesh.SetTriangles(tris, 0);
            _slimeMesh.RecalculateBounds();
        }

        Vector3 ScreenToTileCam(Vector3 worldPos)
        {
            double tx = (worldPos.x / (TW * 0.5) + (-worldPos.y) / (TH * 0.5)) * 0.5;
            double ty = (-worldPos.y / (TH * 0.5) - worldPos.x / (TW * 0.5)) * 0.5;
            return new Vector3((float)tx, (float)ty, 0);
        }

        // ===== Маяк героя (всегда виден) =====
        void UpdateBeacon()
        {
            var p = world.data.player;
            var pos = PosToWorld(p.x, p.y);
            _beacon.mr.transform.position = pos;
            float beat = 0.5f + Mathf.Sin((float)_animT * 2.4f) * 0.5f;
            float rx = 0.55f + beat * 0.10f, ry = (0.55f + beat * 0.10f) * 0.5f;
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            AddEllipse(verts, cols, tris, Vector3.zero, rx, ry, new Color(0.66f, 0.94f, 0.60f, 0.16f + beat * 0.10f), 26);
            AddEllipse(verts, cols, tris, Vector3.zero, rx + 0.06f, ry + 0.03f, new Color(0.75f, 0.98f, 0.66f, 0.10f), 26);
            _beacon.mesh.Clear();
            _beacon.mesh.SetVertices(verts); _beacon.mesh.SetColors(cols); _beacon.mesh.SetTriangles(tris, 0);
            _beacon.mesh.RecalculateBounds();
        }

        // ===== Ночь/сумерки поверх всего =====
        void UpdateOverlay()
        {
            var cl = world.clock;
            float a = 0;
            if (cl.IsNight) a = 0.42f;
            else if (cl.IsDusk) a = 0.18f;
            if (world.DungeonActive) a = Mathf.Max(a, 0.22f);
            if (world.data.senses.unlocked.Length < 3) a = Mathf.Max(a, 0.10f); // слепота чуть темнее
            _overlayMat.color = new Color(0.01f, 0.02f, 0.04f, a);
        }

        void OnDestroy()
        {
            foreach (var b in _creatures) if (b.mesh != null) Destroy(b.mesh);
            if (_beacon.mesh != null) Destroy(_beacon.mesh);
            Destroy(_tileMesh); Destroy(_slimeMesh); Destroy(_overlay);
            Destroy(_mat); Destroy(_slimeMat); Destroy(_overlayMat);
        }

        void Update()
        {
            if (world == null) return;
            double dt = Mathf.Min(Time.deltaTime, 0.033f);
            _animT += dt;
            _exploreTimer -= (float)dt;
            if (_exploreTimer <= 0) { _exploreTimer = 0.25f; RebuildTiles(); }
            UpdateCreatureBlobs();
            UpdateSlime(dt);
            UpdateBeacon();
            UpdateOverlay();
        }
    }
}
