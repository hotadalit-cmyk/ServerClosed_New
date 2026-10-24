// Слой Simulation: определения видов существ региона «Лес Первого Дыхания» (ТЗ §5.1).
// Разделены: биологическое тело (stats), поведение (AI-параметры), духовная сущность (sentient — §8).

import type { SpeciesDef, SpeciesId } from './types';

export const SPECIES: Record<SpeciesId, SpeciesDef> = {
  slug: {
    id: 'slug', name: 'Лесной слизень', sentient: false, lifespan: 12,
    base: { maxHp: 10, atk: 2, def: 0, speed: 1.4, perception: 90 },
    biomass: [3, 6], geneDrop: [], geneChance: 0,
    diet: 'detritivore', aggression: 0, popCap: 14,
  },
  beetle: {
    id: 'beetle', name: 'Жук-скребень', sentient: false, lifespan: 10,
    base: { maxHp: 12, atk: 4, def: 2, speed: 1.2, perception: 80 },
    biomass: [6, 10], geneDrop: ['chitin_plate', 'strong_jaws'], geneChance: 0.55,
    diet: 'detritivore', aggression: 0.35, popCap: 10,
  },
  firefly: {
    id: 'firefly', name: 'Искровый светляк', sentient: false, lifespan: 8,
    base: { maxHp: 7, atk: 3, def: 0, speed: 2.4, perception: 120 },
    biomass: [4, 7], geneDrop: ['electric_organ'], geneChance: 0.5,
    diet: 'detritivore', aggression: 0.2, popCap: 8,
  },
  rabbit: {
    id: 'rabbit', name: 'Полевка', sentient: false, lifespan: 9,
    base: { maxHp: 9, atk: 1, def: 0, speed: 3.0, perception: 150 },
    biomass: [5, 9], geneDrop: ['fast_legs', 'metabolism'], geneChance: 0.45,
    diet: 'herbivore', aggression: 0, popCap: 12,
  },
  deer: {
    id: 'deer', name: 'Косуля', sentient: false, lifespan: 16,
    base: { maxHp: 24, atk: 2, def: 2, speed: 3.2, perception: 170 },
    biomass: [14, 22], geneDrop: ['fast_legs', 'metabolism', 'life_core'], geneChance: 0.5,
    diet: 'herbivore', aggression: 0, popCap: 6,
  },
  wolf: {
    id: 'wolf', name: 'Лесной волк', sentient: false, lifespan: 26,
    base: { maxHp: 44, atk: 8, def: 2, speed: 2.9, perception: 190 },
    biomass: [18, 30], geneDrop: ['wolf_muscle', 'hunter_instinct'], geneChance: 0.6,
    diet: 'carnivore', aggression: 0.75, pack: 2, popCap: 8,
  },
  toad: {
    id: 'toad', name: 'Пепельная жаба', sentient: false, lifespan: 15,
    base: { maxHp: 20, atk: 5, def: 2, speed: 1.3, perception: 110 },
    biomass: [9, 14], geneDrop: ['poison_glands'], geneChance: 0.55,
    diet: 'carnivore', aggression: 0.4, popCap: 7,
  },
  spider: {
    id: 'spider', name: 'Лесной ткач', sentient: false, lifespan: 12,
    base: { maxHp: 15, atk: 4, def: 1, speed: 2.2, perception: 140 },
    biomass: [7, 12], geneDrop: ['web_glands'], geneChance: 0.55,
    diet: 'carnivore', aggression: 0.45, popCap: 7,
  },
  spirit: {
    id: 'spirit', name: 'Лесной дух', sentient: true, lifespan: 9999,
    base: { maxHp: 26, atk: 6, def: 3, speed: 1.8, perception: 220 },
    biomass: [20, 30], geneDrop: ['spirit_resonance', 'life_core'], geneChance: 0.8,
    diet: 'spirit', aggression: 0.15, popCap: 3,
  },
  crystalslug: {
    id: 'crystalslug', name: 'Кристальный слизень', sentient: false, lifespan: 30,
    base: { maxHp: 36, atk: 3, def: 7, speed: 0.9, perception: 100 },
    biomass: [16, 26], geneDrop: ['crystal_armor', 'regeneration'], geneChance: 0.7,
    diet: 'detritivore', aggression: 0.1, popCap: 4,
  },
  mushling: {
    id: 'mushling', name: 'Спорник', sentient: false, lifespan: 20,
    base: { maxHp: 12, atk: 2, def: 1, speed: 0.6, perception: 70 },
    biomass: [5, 8], geneDrop: ['spore_cloud'], geneChance: 0.6,
    diet: 'detritivore', aggression: 0.1, popCap: 9,
  },
  guard: {
    id: 'guard', name: 'Ополченец', sentient: true, lifespan: 55,
    base: { maxHp: 50, atk: 7, def: 5, speed: 2.6, perception: 170 },
    biomass: [0, 0], geneDrop: [], geneChance: 0,
    diet: 'carnivore', aggression: 0, popCap: 0,
  },
  // ===== Жители Грибных Катакомб (Регион 2, §6) =====
  sporeling: {
    id: 'sporeling', name: 'Спорыш', sentient: false, lifespan: 15,
    base: { maxHp: 11, atk: 3, def: 0, speed: 2.0, perception: 110 },
    biomass: [4, 7], geneDrop: ['spore_veil', 'spore_cloud'], geneChance: 0.5,
    diet: 'carnivore', aggression: 0.5, popCap: 0,
  },
  mycenoid: {
    id: 'mycenoid', name: 'Мицелиальный ужас', sentient: false, lifespan: 40,
    base: { maxHp: 30, atk: 5, def: 3, speed: 1.0, perception: 90 },
    biomass: [12, 20], geneDrop: ['mycelial_bond'], geneChance: 0.65,
    diet: 'carnivore', aggression: 0.6, popCap: 0,
  },
  myco_monarch: {
    id: 'myco_monarch', name: 'Грибной Монарх', sentient: false, lifespan: 999,
    base: { maxHp: 84, atk: 7, def: 4, speed: 1.15, perception: 150 },
    biomass: [40, 60], geneDrop: ['bioluminescent', 'mycelial_bond', 'crystal_vein'], geneChance: 1,
    diet: 'carnivore', aggression: 1, popCap: 0,
  },
  // ===== Жители Кристальных Пещер (Регион 3, §6) =====
  shardling: {
    id: 'shardling', name: 'Осколыш', sentient: false, lifespan: 18,
    base: { maxHp: 16, atk: 4, def: 4, speed: 1.7, perception: 110 },
    biomass: [6, 10], geneDrop: ['shard_skin'], geneChance: 0.5,
    diet: 'carnivore', aggression: 0.4, popCap: 5,
  },
  golem: {
    id: 'golem', name: 'Кристальный страж', sentient: false, lifespan: 300,
    base: { maxHp: 46, atk: 6.5, def: 6, speed: 0.8, perception: 120 },
    biomass: [16, 26], geneDrop: ['geo_sense', 'crystal_armor'], geneChance: 0.6,
    diet: 'carnivore', aggression: 0.5, popCap: 0,
  },
  crystal_titan: {
    id: 'crystal_titan', name: 'Кристальный Титан', sentient: false, lifespan: 9999,
    base: { maxHp: 92, atk: 7.5, def: 6, speed: 0.85, perception: 160 },
    biomass: [50, 70], geneDrop: ['prismatic_core', 'crystal_armor', 'regeneration'], geneChance: 1,
    diet: 'carnivore', aggression: 1, popCap: 0,
  },
};

export const HUNTABLE: SpeciesId[] = ['slug', 'beetle', 'firefly', 'rabbit', 'deer', 'wolf', 'toad', 'spider', 'spirit', 'crystalslug', 'mushling', 'sporeling', 'mycenoid', 'myco_monarch', 'shardling', 'golem', 'crystal_titan'];
