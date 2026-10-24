// SERVERCLOSED — Core: статические таблицы данных (порт creatures.ts, genes.ts, digestion.ts, senses.ts).
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    // ===== Виды существ (ТЗ §5.1, 18 видов) =====
    [Serializable]
    public class SpeciesDef
    {
        public string id; public string name;
        public bool sentient;          // разумная душа → карма за убийство (§12)
        public double lifespan;        // дни
        public double maxHp, atk, def, speed, perception;
        public double bioMin, bioMax;  // диапазон биомассы
        public string[] geneDrop;      // возможные гены
        public double geneChance;
        public string diet;            // herbivore | carnivore | detritivore | spirit
        public double aggression;      // 0..1
        public int popCap;             // потолок популяции надземья
    }

    public static class Species
    {
        public static readonly Dictionary<string, SpeciesDef> All = Build();

        static Dictionary<string, SpeciesDef> Build()
        {
            var m = new Dictionary<string, SpeciesDef>();
            void S(string id, string name, bool sent, double life,
                double hp, double atk, double def, double spd, double per,
                double bmin, double bmax, string[] genes, double gch,
                string diet, double aggr, int cap)
            {
                var d = new SpeciesDef
                {
                    id = id, name = name, sentient = sent, lifespan = life,
                    maxHp = hp, atk = atk, def = def, speed = spd, perception = per,
                    bioMin = bmin, bioMax = bmax, geneDrop = genes, geneChance = gch,
                    diet = diet, aggression = aggr, popCap = cap
                };
                m[id] = d;
            }

            S("slug", "Лесной слизень", false, 12, 10, 2, 0, 1.4, 90, 3, 6, new string[0], 0, "detritivore", 0, 14);
            S("beetle", "Жук-скребень", false, 10, 12, 4, 2, 1.2, 80, 6, 10, new[] { "chitin_plate", "strong_jaws" }, 0.55, "detritivore", 0.35, 10);
            S("firefly", "Искровый светляк", false, 8, 7, 3, 0, 2.4, 120, 4, 7, new[] { "electric_organ" }, 0.5, "detritivore", 0.2, 8);
            S("rabbit", "Полевка", false, 9, 9, 1, 0, 3.0, 150, 5, 9, new[] { "fast_legs", "metabolism" }, 0.45, "herbivore", 0, 12);
            S("deer", "Косуля", false, 16, 24, 2, 2, 3.2, 170, 14, 22, new[] { "fast_legs", "metabolism", "life_core" }, 0.5, "herbivore", 0, 6);
            S("wolf", "Лесной волк", false, 26, 44, 8, 2, 2.9, 190, 18, 30, new[] { "wolf_muscle", "hunter_instinct" }, 0.6, "carnivore", 0.75, 8);
            S("toad", "Пепельная жаба", false, 15, 20, 5, 2, 1.3, 110, 9, 14, new[] { "poison_glands" }, 0.55, "carnivore", 0.4, 7);
            S("spider", "Лесной ткач", false, 12, 15, 4, 1, 2.2, 140, 7, 12, new[] { "web_glands" }, 0.55, "carnivore", 0.45, 7);
            S("spirit", "Лесной дух", true, 9999, 26, 6, 3, 1.8, 220, 20, 30, new[] { "spirit_resonance", "life_core" }, 0.8, "spirit", 0.15, 3);
            S("crystalslug", "Кристальный слизень", false, 30, 36, 3, 7, 0.9, 100, 16, 26, new[] { "crystal_armor", "regeneration" }, 0.7, "detritivore", 0.1, 4);
            S("mushling", "Спорник", false, 20, 12, 2, 1, 0.6, 70, 5, 8, new[] { "spore_cloud" }, 0.6, "detritivore", 0.1, 9);
            S("guard", "Ополченец", true, 55, 50, 7, 5, 2.6, 170, 0, 0, new string[0], 0, "carnivore", 0, 0);
            // Грибные Катакомбы (Регион 2)
            S("sporeling", "Спорыш", false, 15, 11, 3, 0, 2.0, 110, 4, 7, new[] { "spore_veil", "spore_cloud" }, 0.5, "carnivore", 0.5, 0);
            S("mycenoid", "Мицелиальный ужас", false, 40, 30, 5, 3, 1.0, 90, 12, 20, new[] { "mycelial_bond" }, 0.65, "carnivore", 0.6, 0);
            S("myco_monarch", "Грибной Монарх", false, 999, 84, 7, 4, 1.15, 150, 40, 60, new[] { "bioluminescent", "mycelial_bond", "crystal_vein" }, 1, "carnivore", 1, 0);
            // Кристальные Пещеры (Регион 3)
            S("shardling", "Осколыш", false, 18, 16, 4, 4, 1.7, 110, 6, 10, new[] { "shard_skin" }, 0.5, "carnivore", 0.4, 5);
            S("golem", "Кристальный страж", false, 300, 46, 6.5, 6, 0.8, 120, 16, 26, new[] { "geo_sense", "crystal_armor" }, 0.6, "carnivore", 0.5, 0);
            S("crystal_titan", "Кристальный Титан", false, 9999, 92, 7.5, 6, 0.85, 160, 50, 70, new[] { "prismatic_core", "crystal_armor", "regeneration" }, 1, "carnivore", 1, 0);
            return m;
        }

        public static SpeciesDef Get(string id) { SpeciesDef d; return All.TryGetValue(id, out d) ? d : null; }
    }

    // ===== Гены (ТЗ §9) =====
    [Serializable]
    public class GeneDef
    {
        public string id; public string name; public string category;
        public string desc;
        public double cost, energy, instability;
        public double maxHp, atk, def, speed, perception, energyMax, hpRegen, lifeDays;
        public string source;
    }

    public static class Genes
    {
        public static readonly Dictionary<string, GeneDef> All = Build();

        static void M(GeneDef g, string id, string name, string cat, double cost, double en, double inst,
            double hp = 0, double atk = 0, double def = 0, double spd = 0, double per = 0,
            double emax = 0, double regen = 0, double life = 0, string src = "")
        {
            g.id = id; g.name = name; g.category = cat; g.cost = cost; g.energy = en; g.instability = inst;
            g.maxHp = hp; g.atk = atk; g.def = def; g.speed = spd; g.perception = per;
            g.energyMax = emax; g.hpRegen = regen; g.lifeDays = life; g.source = src;
        }

        static Dictionary<string, GeneDef> Build()
        {
            var m = new Dictionary<string, GeneDef>();
            GeneDef g;

            g = new GeneDef(); M(g, "slime_coat", "Слизистый покров", "physical", 0, 1, 0, hp: 4, def: 1, src: "Слизни"); m[g.id] = g;
            g = new GeneDef(); M(g, "sensors", "Химические сенсоры", "instinct", 0, 1, 0, per: 40, src: "Слизни"); m[g.id] = g;
            g = new GeneDef(); M(g, "chitin_plate", "Хитиновый панцирь", "physical", 25, 2, 6, hp: 10, def: 3, spd: -0.15, src: "Жуки-скребни"); m[g.id] = g;
            g = new GeneDef(); M(g, "strong_jaws", "Сильные челюсти", "physical", 20, 2, 5, atk: 4, src: "Жуки-скребни"); m[g.id] = g;
            g = new GeneDef(); M(g, "wolf_muscle", "Волчья мощь", "physical", 40, 3, 12, hp: 8, atk: 7, spd: 0.25, src: "Лесные волки"); m[g.id] = g;
            g = new GeneDef(); M(g, "hunter_instinct", "Охотничий инстинкт", "instinct", 30, 2, 8, per: 60, atk: 2, src: "Лесные волки"); m[g.id] = g;
            g = new GeneDef(); M(g, "fast_legs", "Быстрые ноги", "physical", 18, 2, 4, spd: 0.45, src: "Полевки, косули"); m[g.id] = g;
            g = new GeneDef(); M(g, "metabolism", "Эффективный метаболизм", "instinct", 22, 1, 3, emax: 4, regen: 0.6, life: 2, src: "Полевки, косули"); m[g.id] = g;
            g = new GeneDef(); M(g, "electric_organ", "Электрогенератор", "elemental", 35, 3, 12, atk: 5, src: "Искровые светлячки"); m[g.id] = g;
            g = new GeneDef(); M(g, "web_glands", "Паутинные железы", "elemental", 30, 2, 9, def: 2, src: "Лесные ткачи"); m[g.id] = g;
            g = new GeneDef(); M(g, "poison_glands", "Ядовитые железы", "elemental", 32, 2, 10, atk: 3, src: "Пепельные жабы"); m[g.id] = g;
            g = new GeneDef(); M(g, "spore_cloud", "Споровое облако", "elemental", 28, 2, 8, hp: 6, src: "Спорники"); m[g.id] = g;
            g = new GeneDef(); M(g, "regeneration", "Регенерация", "crystal", 45, 3, 11, regen: 1.2, life: 2, src: "Кристальные слизни"); m[g.id] = g;
            g = new GeneDef(); M(g, "crystal_armor", "Кристаллическая броня", "crystal", 50, 3, 13, hp: 14, def: 7, spd: -0.25, life: 2, src: "Кристальные слизни"); m[g.id] = g;
            g = new GeneDef(); M(g, "spirit_resonance", "Духовный резонанс", "spirit", 40, 2, 5, per: 30, life: 3, src: "Лесные духи"); m[g.id] = g;
            g = new GeneDef(); M(g, "life_core", "Ядро жизненной силы", "spirit", 38, 2, 6, hp: 18, regen: 0.5, emax: 3, src: "Лесные духи"); m[g.id] = g;
            g = new GeneDef(); M(g, "bioluminescent", "Изначальное свечение", "elemental", 42, 2, 7, per: 55, atk: 1, src: "Грибной Монарх"); m[g.id] = g;
            g = new GeneDef(); M(g, "mycelial_bond", "Мицелиальная связь", "instinct", 30, 2, 5, regen: 0.9, hp: 8, src: "Мицелиальные ужасы"); m[g.id] = g;
            g = new GeneDef(); M(g, "spore_veil", "Споровая вуаль", "elemental", 26, 2, 6, def: 2, hp: 4, src: "Спорыши"); m[g.id] = g;
            g = new GeneDef(); M(g, "crystal_vein", "Кристальные жилы", "crystal", 44, 3, 10, def: 4, atk: 2, spd: -0.1, life: 1, src: "Грибной Монарх"); m[g.id] = g;
            g = new GeneDef(); M(g, "shard_skin", "Осколочная шкура", "crystal", 22, 2, 6, def: 3, spd: -0.05, src: "Осколыши"); m[g.id] = g;
            g = new GeneDef(); M(g, "geo_sense", "Гео-сенсоры", "instinct", 28, 2, 5, per: 70, atk: 1, src: "Кристальные стражи"); m[g.id] = g;
            g = new GeneDef(); M(g, "prismatic_core", "Призматическое ядро", "crystal", 55, 3, 11, hp: 16, def: 3, emax: 3, regen: 0.5, life: 1, src: "Кристальный Титан"); m[g.id] = g;
            return m;
        }
    }

    // ===== Синергии (§9.2) =====
    [Serializable]
    public class SynergyDef
    {
        public string id; public string name;
        public string geneA, geneB;
        public string desc;
        public double maxHp, atk, def, speed, perception, energyMax, hpRegen, lifeDays;
        public string special;   // stun | toxicAura | corrosion | battery | null
    }

    public static class Synergies
    {
        public static readonly SynergyDef[] All =
        {
            new SynergyDef { id="electric_trap", name="Электрическая ловушка", geneA="electric_organ", geneB="web_glands", desc="Паутина проводит разряд: удары иногда парализуют.", atk=2, special="stun" },
            new SynergyDef { id="toxic_zone", name="Токсичное заражение", geneA="poison_glands", geneB="spore_cloud", desc="Споры несут яд: зона вокруг вас вредит врагам.", maxHp=4, special="toxicAura" },
            new SynergyDef { id="fast_regen", name="Быстрое восстановление", geneA="regeneration", geneB="life_core", desc="Раны затягиваются втрое быстрее.", hpRegen=2.0, maxHp=8 },
            new SynergyDef { id="corrosion_shield", name="Коррозионная защита", geneA="poison_glands", geneB="chitin_plate", desc="Панцирь сочится кислотой: ответный урон.", def=2, special="corrosion" },
            new SynergyDef { id="living_battery", name="Живая батарея", geneA="crystal_armor", geneB="electric_organ", desc="Повышенный запас энергии.", energyMax=6, atk=2, special="battery" },
            new SynergyDef { id="apex_sense", name="Чутьё апекса", geneA="hunter_instinct", geneB="sensors", desc="Вы чувствуете всё живое.", perception=50, speed=0.1 },
            new SynergyDef { id="myco_network", name="Мицелиальная сеть", geneA="mycelial_bond", geneB="spore_cloud", desc="Регенерация втрое глубже.", hpRegen=1.5, maxHp=10 },
            new SynergyDef { id="crystal_harmony", name="Кристальная гармония", geneA="crystal_armor", geneB="prismatic_core", desc="Броня — вторая шкура, жизнь — как камень.", def=4, maxHp=14, lifeDays=2 },
        };

        public static readonly string[,] Conflicts = new string[,]
        {
            { "crystal_armor", "fast_legs" },
            { "crystal_vein", "fast_legs" },
            { "chitin_plate", "slime_coat" },
            { "spore_cloud", "crystal_armor" },
            { "prismatic_core", "spore_cloud" },
        };
    }

    // ===== Пищеварение: классы вещества (§6.2), состав добычи, режимы (§6.6) =====
    public static class Food
    {
        // kind → name, compat, residue
        public static readonly Dictionary<string, double[]> KindInfo = new Dictionary<string, double[]>
        {
            { "organic", new[] { 1.0, 0.06 } },
            { "mineral", new[] { 0.55, 0.16 } },
            { "magic",   new[] { 0.5, 0.10 } },
            { "toxic",   new[] { 0.7, 0.08 } },
        };

        public static string TintOf(string kind)
        {
            if (kind == "mineral") return "150,170,192";
            if (kind == "magic") return "168,138,224";
            if (kind == "toxic") return "122,138,74";
            return "154,200,96";
        }

        // вид → kind, hardness, toxicity
        public static Dictionary<string, double[]> Prey = new Dictionary<string, double[]>
        {
            { "slug", new[] { 0.0, 1.0, 0.0 } },          // kind: 0=organic 1=mineral 2=magic 3=toxic
            { "beetle", new[] { 0.0, 1.2, 0.0 } },
            { "mushling", new[] { 0.0, 1.0, 0.05 } },
            { "mycenoid", new[] { 0.0, 1.0, 0.10 } },
            { "sporeling", new[] { 3.0, 1.0, 0.35 } },
            { "myco_monarch", new[] { 0.0, 1.6, 0.15 } },
            { "toad", new[] { 0.0, 1.1, 0.10 } },
            { "spider", new[] { 3.0, 1.2, 0.40 } },
            { "rabbit", new[] { 0.0, 1.0, 0.0 } },
            { "deer", new[] { 0.0, 1.3, 0.0 } },
            { "wolf", new[] { 0.0, 1.4, 0.08 } },
            { "firefly", new[] { 2.0, 1.0, 0.0 } },
            { "spirit", new[] { 2.0, 1.8, 0.20 } },
            { "crystalslug", new[] { 1.0, 2.2, 0.0 } },
            { "shardling", new[] { 1.0, 2.6, 0.0 } },
            { "golem", new[] { 1.0, 3.4, 0.0 } },
            { "crystal_titan", new[] { 1.0, 4.4, 0.0 } },
            { "guard", new[] { 0.0, 1.2, 0.0 } },
        };

        public static string KindName(int k) { return k == 1 ? "mineral" : k == 2 ? "magic" : k == 3 ? "toxic" : "organic"; }

        // режимы: name, speedMul, geneMul, toxMul, energyDrain, unlockAt
        public const double FAST_UNLOCK_AT = 25;
        public const double GENTLE_UNLOCK_AT = 45;
    }

    // ===== Восприятие: пороги органов и диета (Дополнение к ТЗ) =====
    public static class SensesData
    {
        public static readonly string[] ORDER = { "touch", "chemo", "vibration", "thermal", "light", "vision", "hearing" };

        // порог усвоенной биомассы (touch врождённое, spirit — от руды)
        public static readonly Dictionary<string, double> Thresholds = new Dictionary<string, double>
        {
            { "chemo", 8 }, { "vibration", 18 }, { "thermal", 30 },
            { "light", 44 }, { "vision", 60 }, { "hearing", 80 },
        };

        public const double ORE_CORE_THRESHOLD = 6;
        public const double ORE_OVERLOAD = 10;
        public const double MAGIC_ENERGY_MAX = 16;
        public const double IMPRINT_LIFE = 8;

        // руда по видам (п.8)
        public static readonly Dictionary<string, double> OreYield = new Dictionary<string, double>
        {
            { "crystalslug", 2 }, { "shardling", 2 }, { "golem", 3 }, { "crystal_titan", 6 }, { "spirit", 3 },
        };

        // диета: что кормит какие органы (вид → [organ, gain, ...])
        public static readonly Dictionary<string, double[]> DietFeed = new Dictionary<string, double[]>
        {
            { "slug", new[] { 1, 0.9, 2, 0.25 } },              // chemo, vibration
            { "beetle", new[] { 1, 0.7, 2, 0.7 } },
            { "mushling", new[] { 1, 1.0, 5, 0.15 } },          // chemo, vision
            { "mycenoid", new[] { 1, 1.0, 4, 0.2 } },           // chemo, light
            { "sporeling", new[] { 1, 0.9, 4, 0.25 } },
            { "myco_monarch", new[] { 1, 1.4, 5, 0.4 } },
            { "spider", new[] { 2, 1.0, 1, 0.3 } },             // vibration, chemo
            { "toad", new[] { 2, 0.7, 3, 0.35 } },              // vibration, thermal
            { "rabbit", new[] { 3, 0.9, 6, 0.4 } },             // thermal, hearing
            { "deer", new[] { 3, 1.1, 6, 0.5 } },
            { "wolf", new[] { 3, 1.0, 6, 0.8, 2, 0.4 } },
            { "firefly", new[] { 4, 1.2, 5, 0.6 } },            // light, vision
            { "crystalslug", new[] { 4, 1.0, 5, 0.7 } },
            { "shardling", new[] { 4, 0.8, 5, 0.8 } },
            { "golem", new[] { 5, 1.0, 4, 0.5, 2, 0.4 } },
            { "crystal_titan", new[] { 5, 1.6, 4, 1.0 } },
            { "guard", new[] { 6, 1.2, 3, 0.6 } },              // hearing, thermal
            { "spirit", new[] { 4, 0.8, 6, 0.6 } },
            { "cache_mushroom", new[] { 1, 0.8, 2, 0.4 } },
            { "cache_crystal", new[] { 4, 0.9, 5, 0.7 } },
            { "cache_river", new[] { 1, 0.6, 2, 0.4, 3, 0.3 } },
        };

        public const double UNIVERSAL_TRICKLE = 0.12;
        public const double FOCUS_MUL = 1.75;

        public static int OrganIndex(string organ)
        {
            for (int i = 0; i < ORDER.Length; i++) if (ORDER[i] == organ) return i;
            return -1;
        }

        public static readonly string[] OrganNames = {
            "Осязание", "Химическое", "Вибрационное", "Тепловое", "Светочувств.", "Зрение", "Слух"
        };
    }

    // ===== Узлы эволюции (§9) =====
    public static class NodePlan
    {
        public static readonly double[] Costs = { 30, 55, 90, 140 };
        public static double? NextCost(int nodes) { return nodes < Costs.Length ? Costs[nodes] : (double?)null; }
    }
}
