// SERVERCLOSED — Core: «Чаша Отголосов» — точная география мира
// (порт Reference/WebPrototype/src/game/atlas.ts, Дополнение к ТЗ: карта мира).
// Единый источник правды: данные региона, river/fault, тайная трещина, критические точки.
//  п.10.1 — у каждого региона есть экологическая функция (поле ecology);
//  п.10.2 — нет абсолютного деления по силе: в опасных регионах живут слабые организмы;
//  п.6/9  — регион появляется на карте ТОЛЬКО когда игрок его ощутил (worldRegions).
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    public class WorldRegionDef
    {
        public string id, name, layer, difficulty, ecology, desc;
        public double x, y, r;      // центр зоны на игровой карте 52×52 и радиус
        public bool sealedArea;     // запечатанные Глубины на карте не открываются

        public WorldRegionDef(string id, string name, string layer, double x, double y, double r,
            string difficulty, string ecology, string desc, bool sealedRegion = false)
        {
            this.id = id; this.name = name; this.layer = layer;
            this.x = x; this.y = y; this.r = r;
            this.difficulty = difficulty; this.ecology = ecology; this.desc = desc;
            this.sealedArea = sealedRegion;
        }
    }

    public static class ScAtlas
    {
        // ===== Поверхность — 9 регионов + Разлом (место силы на поверхности) =====
        public static readonly WorldRegionDef[] SurfaceRegions =
        {
            new WorldRegionDef("echo_ridge", "Гребень Эха", "surface", 26, 5, 10.5, "опасно",
                "Источник рек и каменистых убежищ; вибрационные рецепторы здесь спасают жизни.",
                "Холодные северные горы над впадиной. Пещерные хищники, скалы, обвалы."),
            new WorldRegionDef("ore_slopes", "Рудные склоны", "surface", 7, 7, 8.5, "умеренно",
                "Выходы магической руды у северного разлома; минеральная пища кристальных форм.",
                "Северо-западные склоны с обнажениями магической руды."),
            new WorldRegionDef("black_ford", "Чёрный брод", "surface", 42, 8, 7.5, "опасно",
                "Единственный безопасный переход через северный разлом; маршрут миграций хищников.",
                "Разрушенные укрепления у перехода через разлом."),
            new WorldRegionDef("root_thicket", "Корневая чаща", "surface", 9, 40, 8, "умеренно",
                "Грибные колонии и симбионты — богатейший источник генетического материала.",
                "Юго-западная чаща гигантских корней."),
            new WorldRegionDef("sunken_ruins", "Затонувшие руины", "surface", 13, 17, 7, "загадочно",
                "Затопленные залы древнего города: редкие материалы, археологические тайны.",
                "Западные руины, поглощённые водой."),
            new WorldRegionDef("forest_edge", "Опушка", "surface", 26, 24, 8.5, "спокойно",
                "Первое поселение разумных: торговля, задания, оборона в Magic Chess.",
                "Сердце впадины. Поляна поселения у старого колодца."),
            new WorldRegionDef("ash_fields", "Пепельные поля", "surface", 40, 24, 8, "опасно",
                "Охотничьи угодья стай; территориальные войны держат популяции в равновесии.",
                "Восточные выжженные поля у волчьих нор."),
            new WorldRegionDef("warm_backwaters", "Тёплые заводи", "surface", 38, 36, 7.5, "спокойно",
                "Мелководье с водорослями и микроорганизмами — почти безопасная пища.",
                "Юго-восточный пруд и мелкие заводи."),
            new WorldRegionDef("mud_bogs", "Илистые топи", "surface", 24, 45, 7.5, "коварно",
                "Дельта реки: органика, ядовитые растения, крупные амфибии, паразиты.",
                "Южные топи, куда стекает вся вода Чаши."),
            new WorldRegionDef("soul_rift", "Разлом забытых душ", "surface", 45, 45, 6, "предельно",
                "Выход магических потоков на поверхность; возрождение духов, следы событий.",
                "Юго-восточная рана мира."),
        };

        // ===== Подземный мир — 6 регионов (родина слизней; п.3 карты мира) =====
        public static readonly WorldRegionDef[] UndergroundRegions =
        {
            new WorldRegionDef("quiet_cradle", "Тихая колыбель", "underground", 26, 16, 8, "низкая",
                "Мягкий грунт, конденсат, микроорганизмы — инкубатор первых жизней.",
                "Первое рождение большинства слизней."),
            new WorldRegionDef("root_garden", "Сад глубинных корней", "underground", 11, 34, 8, "умеренная",
                "Колонии грибов и насекомых: огромный генетический материал, паразитизм, симбиозы.",
                "Грибные Катакомбы прорастают сквозь Корневую чащу."),
            new WorldRegionDef("bone_narrows", "Костяные теснины", "underground", 36, 18, 8, "высокая",
                "Окаменевшие останки гигантов; дефицит пищи дисциплинирует экосистему.",
                "Тесные проходы в костях исполинов у Пепельных полей."),
            new WorldRegionDef("forgotten_vein", "Забытая жила", "underground", 9, 10, 8, "экстремальная",
                "Древние шахты и кристаллы: минеральная пища, магически изменённые существа.",
                "Кристальные Пещеры у Рудных склонов."),
            new WorldRegionDef("underground_channel", "Подземное русло", "underground", 26, 30, 8, "переменчивая",
                "Центральная транспортная артерия: реки приносят пищу всех биомов и разносят споры.",
                "Затопленные тоннели под всей Чашей."),
            new WorldRegionDef("soul_rift_deep", "Разлом забытых душ", "underground", 40, 40, 8, "предельная",
                "Аномальная магия, остаточные духовные отпечатки, редчайшие формы жизни.",
                "Глубинная часть Разлома."),
        };

        // ===== Глубины отголосков — 2 особые области (Этап 4, запечатаны) =====
        public static readonly WorldRegionDef[] DepthRegions =
        {
            new WorldRegionDef("echo_archive", "Архив отголосков", "depths", 18, 40, 7, "непостижимо",
                "Кристаллы-хроники сохраняют отпечатки существ, катастроф и цивилизаций.",
                "Слой памяти мира.", true),
            new WorldRegionDef("rift_heart", "Сердце разлома", "depths", 40, 40, 7, "непостижимо",
                "Пик концентрации магии: влияет на генетику, долголетие и стабильность души.",
                "Самая глубокая исследуемая область мира.", true),
        };

        public static readonly WorldRegionDef[] AllRegions = Build();

        static WorldRegionDef[] Build()
        {
            var all = new List<WorldRegionDef>();
            all.AddRange(SurfaceRegions);
            all.AddRange(UndergroundRegions);
            all.AddRange(DepthRegions);
            return all.ToArray();
        }

        public static WorldRegionDef ById(string id)
        {
            foreach (var r in AllRegions) if (r.id == id) return r;
            return null;
        }

        /// К какому поверхностному региону принадлежит точка: ближайший центр
        /// (арены не изолированы — переходные экотоны).
        public static WorldRegionDef RegionAt(double x, double y)
        {
            WorldRegionDef best = SurfaceRegions[0];
            double bestD = double.PositiveInfinity;
            foreach (var r in SurfaceRegions)
            {
                double d = (r.x - x) * (r.x - x) + (r.y - y) * (r.y - y);
                if (d < bestD) { bestD = d; best = r; }
            }
            return best;
        }

        /// Подземный регион, которому соответствует активное подземелье (для открытия карты).
        public static string DungeonRegionId(string id)
        {
            if (id == "catacombs") return "root_garden";
            if (id == "crystal_caves") return "forgotten_vein";
            if (id == "underground_channel") return "underground_channel";
            if (id == "birth_nest") return "quiet_cradle";   // родовая норка — колыбель души
            return "underground_channel";
        }

        // ===== Река Чаши и северный разлом (для карты/атласа) =====
        public static readonly double[][] RiverWaypoints =
        {
            new[] { 25.0, 3.0 }, new[] { 23.0, 9.0 }, new[] { 27.0, 15.0 }, new[] { 22.0, 21.0 },
            new[] { 24.0, 28.0 }, new[] { 21.0, 35.0 }, new[] { 23.0, 41.0 }, new[] { 24.0, 46.0 },
        };

        public static readonly double[][] FaultLine =
        {
            new[] { 43.0, 12.0 }, new[] { 44.0, 18.0 }, new[] { 43.0, 26.0 },
            new[] { 44.0, 33.0 }, new[] { 45.0, 39.0 }, new[] { 45.0, 44.0 },
        };

        // ===== Тайная трещина: узкий проход, доступный только малому телу
        // (п.3 карты: «маленький слизень пролезет туда, куда не пролезет эволюционировавшая форма») =====
        public const int CRACK_MAX_ORGANS = 1;   // тело с ≤1 установленным геном пролезает

        public class CrackSpot
        {
            public double pocketX, pocketY;      // центр кармана (внутри — еда, недоступная большим формам)
            public double entranceX, entranceY;  // вход-трещина на кольце
            public double rIn, rOut;             // внутренний радиус (пол) и внешний радиус кольца скалы
        }

        public static readonly CrackSpot[] CrackSpots =
        {
            new CrackSpot { pocketX = 34, pocketY = 31, entranceX = 34, entranceY = 33.6, rIn = 1.9, rOut = 2.7 },
        };

        // Критические точки: рядом с ними нельзя красить непроходимое (горы/вода).
        public static readonly double[][] CriticalPoints =
        {
            new[] { 19.5, 30.5 }, new[] { 11.2, 41.6 }, new[] { 43.6, 27.4 },
            new[] { 9.6, 10.0 }, new[] { 42.8, 42.8 }, new[] { 11.4, 38.4 },
            new[] { 9.4, 8.6 }, new[] { 33.8, 42.2 }, new[] { 26.0, 24.0 },
        };

        /// Масштаб для атласа/документации: игровой сектор ≈ 780 м по стороне (Этап 1 — 500–700 м+).
        public const int TILE_METERS = 15;
        public const int SECTOR_METERS = 52 * TILE_METERS;
    }
}
