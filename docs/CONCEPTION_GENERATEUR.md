# Conception du générateur de cartes — Map Heroic

> Document de conception final, destiné à être validé par le client puis à guider l'implémentation étape par étape. Il est autonome : il ne suppose aucune lecture des conceptions candidates, des évaluations du jury ni des relectures sceptiques dont les corrections ont été intégrées directement dans chaque section. Le principe retenu est « une seule géométrie, du premier tirage au navmesh », enrichi d'une hydrologie préalable, de chaînes de frontières typées par segment, d'une recherche locale pour les groupes, d'un test de navigation local par arête, d'une campagne de seeds et d'un hash multi-plateformes vérifié dès le premier jalon.

---

## 1. Résumé des décisions

| Décision | Choix retenu | Justification courte |
|---|---|---|
| Principe directeur | **Une seule géométrie** : un maillage de cellules fines (Voronoi relaxé) porte la classification mer/terre, le relief, les 90 zones, les groupes, les frontières naturelles, le mesh 3D et les sources du navmesh | Aucune re-projection grille → polygones, obstacles du navmesh exacts par construction, une seule structure à déboguer pour un développeur solo |
| Ordre des étapes | Géométrie → île → **relief et hydrologie** → zones → groupes → passages → matérialisation des frontières (avec sockets) → **départs (critères structurels)** → terrains et ressources → validation | Le relief précède les zones (leurs frontières l'épousent) ; les départs sont choisis avant les terrains pour que les contraintes désert/marais autour des départs soient appliquées en une seule passe |
| Échantillonnage des cellules | Poisson-disc (Bridson, k = 30) rayon **14 m** sur **1400 × 1400 m**, Delaunay (DelaunatorSharp adapté) + Lloyd × 2 → **≈ 6 200 cellules mesurées** (5 900-6 600 ; l'estimation initiale de 7 000 supposait un taux de remplissage trop élevé), ≈ 1 900 sur terre, ≈ 21 par zone | Contours de zones organiques sans grille, granularité des cols/gués ≈ 14 m, équilibrage d'aire fin ; rayon 16 m conservé en paramètre de comparaison |
| Taille de la carte | 1400 × 1400 m, île ≈ 31 % de la surface (fenêtre acceptée 26-38 %), aire d'île ≈ 610 000 m², zone moyenne ≈ 6 750 m² | Une unité à 4 m/s traverse une zone (≈ 82 m) en ≈ 20 s : échelle Northgard ; l'île reste entourée d'une marge de mer ≥ 48 m (nominal ≈ 84 m) |
| Obtention des 90 zones | **Agrégation** de cellules fines à partir de 90 germes répartis par échantillonnage du point le plus éloigné puis **relaxés deux fois** (recentrage sur le centre de masse de la zone), croissance « la plus petite d'abord » dont la clé **ajoute** le coût du relief au lieu de le multiplier, puis transferts de cellules de bord avec contrainte de dureté **molle** | Compte exact, connexité par construction. Mesuré sur 25 graines : 0 échec, écart max **24,2 %**, **100 %** des zones dans ± 25 %, 113 ms au pire. La forme multiplicative de la clé, essayée d'abord, créait mécaniquement des écarts de 300 % ; la répartition au prorata des bassins s'est révélée inutile une fois les germes relaxés |
| Groupes de 3-4 zones | Décomposition 3a + 4b = 90 libre, initialisation gloutonne, **recherche locale** (énergie : tailles, connexité, dureté des frontières, rivières naturelles intra-groupe, compacité), réparation des orphelines par **dissolution locale et re-partition exhaustive**, retirage borné | E < 1000 ⇒ zéro violation ; les frontières de groupes coïncident avec les barrières naturelles déjà présentes |
| Passages | Arêtes de Gg éligibles si la chaîne commune mesure ≥ 45 m ; arbre couvrant à degré ≤ 3 (Kruskal borné sur le sous-graphe éligible, vérifié connexe), degré cible tiré dans {1 : 25 %, 2 : 45 %, 3 : 30 %}, passage = suite minimale de 2 à 4 arêtes fines de longueur ≥ 28 m centrée sur le point de crête/flux minimal ; cellules de passage **réservées** (jamais massif, lac ni lit de rivière) | Connexité et borne 1-3 garanties ; largeur réelle du col ≥ 24 m vérifiée ; **jamais** de relâchement à degré 4 |
| Frontières naturelles | Assemblées en **chaînes** coupées aux jonctions (≥ 3 groupes) et aux passages, typées **par segment** selon le relief préexistant (rivière naturelle si le flux y passe, massif par **rang** de crête — 6 à 10 massifs —, bras de mer près de la côte, sinon rivière prolongée par descente) ; massifs prolongés et effilés uniquement aux extrémités mer/jonction | 100 % des arêtes inter-groupes hors passage sont bloquantes, sans falaise artificielle ; la lecture « obstacle = frontière » est cassée par le relief sous-jacent et les prolongements |
| Rivières | Hydrologie réelle avant les zones (comblement des dépressions + accumulation de flux sur les coins Voronoi), largeur = f(√flux) conservée sur **tout** le cours ; rivières frontières bloquantes ; segments intra-groupe franchissables par **gués tous les 3-4 coins** (poisson conservé) ; plancher des coins de rivière à 1,5 m, pas monotone 0,05 m, terminaison en lac si le plancher est atteint | Cours d'eau crédibles qui descendent vraiment vers la mer, jamais sous le niveau de la mer, tout en gardant la règle « ce qui bloque est une frontière » |
| Montagnes et lacs | Sous-ensembles de cellules fines **appartenant à leur zone** (aucun changement de zone après P5), drapeau NonConstructible ; bande de massif asymétrique (2 cellules côté zone Montagne, 1 côté opposé) ; plafond 45 % de cellules Massif + Lac par zone ; lacs de 3 à 6 cellules ; **sockets de mines** au pied de chaque massif dans sa zone Montagne (2-4) ; connexité des cellules praticables vérifiée par zone et par groupe | Englobement sans géométrie supplémentaire, mines placées de façon déterministe, zones et départs jamais coupés en deux |
| Terrains | Un enum par zone ; quota Montagne = nombre de zones désignées par les massifs (6-10) ; désert et marais **maxima** 6 chacun ; côte ≈ 11 ; le reste réparti au prorata fertile 18 / forêt 18 / colline 12 / argileuse 10 ; bonus de contiguïté (biomes) ; pas de désert/marais dans la zone de départ ni ses voisines directes | Distribution stable et lisible, ressources équitablement réparties, boucle d'affectation bornée |
| Départs | 6 groupes choisis **avant les terrains** sur critères structurels (zone de départ ≥ 85 % constructible, sans lac, une seule composante praticable, massif < 10 % ; groupe sans lac, degré ∈ [2, 3]) par « point le plus éloigné » (graphe ≥ 3 sauts, repli ≥ 2 ; euclidien ≥ 0,75·√(aireÎle/6) ≈ 240 m), score structurel pondéré à ± 20 %, ordre fixe pour 2-5 joueurs | Équité mesurable sur des grandeurs qui comptent (aire constructible, sockets, exposition), affichée dans l'éditeur |
| Déterminisme | PRNG xoshiro128\*\* maison + sous-flux par phase, bruit à hachage entier, aucune fonction transcendante dans les décisions, prédicats Delaunay en **entiers longs**, quantification des coordonnées, hash FNV-1a 64 de la **partie discrète seule** (hash « hauteurs » informatif séparé), spike inter-plateformes au jalon J1, `-ffp-contract=off` sur Android et iOS, budget global de rejeux avec dérivation de seed bornée | Même carte sur Android, iOS et éditeur ; convergence garantie en lobby ; filet de sécurité par envoi du `MapData` compressé (≈ 200 Ko) |
| Mesh 3D | Un éventail par cellule (centre + coins, 7 sommets par cellule sans subdivision), couleur de vertex par zone, normales plates par dérivées écran (ddx/ddy) dans un Shader Graph URP cible **Lit** (Fragment Normal Space = World) ; seules les cellules terre et la première couronne de mer sont maillées ; chunks 5 × 5 de 280 m ; massifs en **crêtes continues** (coins partagés) dans un sous-mesh | ≈ 16 k triangles (≈ 32 k avec subdivision d'arêtes), ≈ 19-35 k sommets, ≈ 16-20 chunks de terrain, aucun atlas de textures nécessaire |
| Eau | Shader Graph **maison** (Unlit transparent, deux teintes, deux bruits en défilement UV monde, écume par seuil animé) sans depth ni opaque texture ; masque de rive = texture R8 512² « DistanceRive » rasterisée en P12 depuis `MapData` et échantillonnée en coordonnées monde par la mer, les lacs, les rivières et le terrain (sable mouillé) | Compatible avec l'asset URP Mobile (pas de prépasse de profondeur), un seul mécanisme pour toutes les eaux |
| Navmesh | Sources `NavMeshBuildSource` construites manuellement (terrain par aire, massifs NotWalkable, **ModifierBox** NotWalkable par segment de rivière, polygones de lac en source Mesh NotWalkable, ModifierBox mer de −8 à +0,3 m), bounds = AABB de l'île, `UpdateNavMeshDataAsync` (150-800 ms sur threads de travail, budget ≤ 1 s, bake par tuiles en plan B), ponts en `NavMeshLink`, bâtiments en `NavMeshObstacle` (carving) | Pas de scan de hiérarchie, obstacles exacts sans dépendre de la géométrie, pont activé sans rebake |
| Outil éditeur | Fenêtre UI Toolkit : seed, preview 2D par calques (Painter2D), panneau de validation R1..R18, « Construire 3D », « Vérifier la navigation » (raycast local par arête inter-groupes + chemins globaux), « Tester N seeds » avec CSV et taux de dérivation de seed | Chaque règle est mesurée, pas espérée |
| Assets | **Deux familles seulement** : **KayKit** (CC0 : Forest pour toute la végétation et les rochers, Medieval Hexagon pour bâtiments et mine, Resource Bits pour marqueurs) + **Quaternius** (CC0 : Ultimate Fantasy RTS pour cultures, stades de Town Center, port si confirmé ; Ultimate Nature pour cactus, palmiers, arbres morts) ; palette unique appliquée par un script d'import spécifié (Oklab, sommets dupliqués par face) ; achat ensuite : KayKit Extra (≈ 25 $) ou bascule complète vers Synty POLYGON | Cohérence visuelle par une famille par catégorie, gratuit d'abord |
| Performance | Génération pure C# (≈ 150 ms) dans un `Task` hors thread principal ; meshes sur le thread principal étalés sur plusieurs frames ; navmesh asynchrone sous un écran de chargement montrant la preview 2D ; cible totale < 1 s, limite 2 s | Marge × 2 sur l'objectif, sans Burst |

---

## 2. Règles à garantir (checklist)

- **R1** — La carte est une **île unique** entourée de mer : une seule composante terrestre, ratio terre entre 26 % et 38 % de la carte, **aucune cellule terre dont le site est à moins de 48 m du bord du domaine ni adjacente à un point de bordure**, aucune cellule d'articulation du graphe terre isolant une composante de ≥ 6 cellules.
- **R2** — **Exactement 90 zones**, chacune connexe et non vide ; aucune cellule ne change de zone après la phase P5.
- **R3** — Aire de chaque zone dans **± 30 % de la médiane** (objectif interne ± 25 %). L'aire inclut les sous-parties massif/lac englobées.
- **R4** — Zones **polygonales organiques** issues d'un Voronoi relaxé ; aucune grille hexagonale ni carrée visible.
- **R5** — Partition complète des 90 zones en **groupes de 3 ou 4 zones adjacentes**, chaque groupe connexe.
- **R6** — Toute arête fine séparant deux groupes est **soit un passage, soit couverte par une frontière naturelle** (massif, rivière, lac, mer) ; les frontières naturelles bloquent le déplacement ; l'aspect reste naturel (dureté moyenne des frontières de groupes **avant matérialisation** ≥ 0,5, seuil révisable après campagne de 200 seeds ; aucune barrière artificielle).
- **R7** — Chaque groupe a entre **1 et 3 passages** ; le graphe des groupes est **connexe** ; chaque passage est praticable (largeur réelle du col entre cellules bloquantes ≥ 24 m, pente < 40°, aucune cellule de passage marquée massif, lac ou lit de rivière non-gué).
- **R8** — Des **emplacements de ponts** existent sur les rivières inter-groupes sans passage (1 par frontière au plus) ; un pont construit ajoute un passage sans compter dans la borne 1-3.
- **R9** — **Un seul type de terrain** par zone.
- **R10** — Montagnes et lacs sont **englobés** dans des zones, **non constructibles** ; chaque zone garde **≥ 55 %** de cellules constructibles (seules les cellules Massif et Lac comptent comme non constructibles ; les rivières imposent une marge d'implantation w/2 + 1 m vérifiée sur l'empreinte du bâtiment) ; les cellules praticables de chaque zone et de chaque groupe forment **une seule composante** contenant tous les sockets ; des **sockets de mines** existent au pied de chaque massif, dans sa zone Montagne.
- **R11** — Ressources par terrain (fertile → nourriture, argileuse → argile, forêt → bois, colline → pierre, montagne → minerai via mines, désert et marais → **aucune**, côte → **aucune ressource propre**) ; **poisson** pour toute zone riveraine de la mer, d'un lac ou d'une rivière (y compris les segments intra-groupe à gués).
- **R12** — Quotas de terrains respectés à ± 2 (désert et marais : maxima 6, jamais dépassés) ; désert et marais jamais adjacents à plus d'une autre zone sans ressource ; chaque massif désigne exactement une zone Montagne qui l'englobe.
- **R13** — **6 départs** équilibrés (score structurel à ± 20 %) et éloignés (≥ 3 passages de distance de graphe, repli ≥ 2 ; distance euclidienne ≥ 0,75·√(aireÎle/6), valeur mesurée affichée) ; degré du groupe de départ ∈ [2, 3] ; zone de départ ≥ 85 % constructible, sans lac, sans massif > 10 %, praticable en une composante ; ordre fixe par seed pour 2 à 5 joueurs ; ni la zone de départ ni ses voisines directes ne sont désert/marais.
- **R14** — **Déterminisme par seed** : même hash de la partie discrète de `MapData` sur éditeur Windows, Android arm64 et iOS ; convergence garantie par un budget global de rejeux et une dérivation de seed bornée (k ≤ 4).
- **R15** — **Navmesh** : déplacement libre sur le terrain, obstacles exacts ; chaque arête fine inter-groupes hors passage bloque localement (raycast navmesh de part et d'autre), chaque passage est ouvert localement, et tout chemin entre deux groupes adjacents reliés par un passage reste dans l'union des deux groupes avec une longueur ≤ 1,5 × la distance directe.
- **R16** — **Performance** : génération + 3D + navmesh < 2 s sur mobile milieu de gamme (cible < 1 s) ; 60 fps en jeu (≤ 250 k triangles de décor visibles à zoom maximal).
- **R17** — **Relief léger** (pente moyenne < 4° hors massifs, pente douce vers la mer), montagnes en relief localisé (faces ≤ 60°), limitation de pente appliquée avant quantification et hash, style low-poly flat-shaded.
- **R18** — **Bordures de pays** : contour coloré généré pour l'ensemble des zones d'un joueur, masqué entre deux zones du même propriétaire, projeté sur le relief.

---

## 3. Principe directeur et pipeline

**Principe** : la géographie de base (forme d'île, relief doux, crêtes, hydrologie) est calculée d'abord sur un maillage unique de cellules fines. Les 90 zones sont ensuite obtenues par agrégation de ces cellules, avec un coût qui rend cher le franchissement d'une crête ou d'une rivière : leurs frontières épousent le relief. Les groupes sont choisis par recherche locale pour que leurs frontières tombent là où une barrière existe déjà. Enfin, chaque frontière de groupe est **matérialisée** (massif, rivière, lac, mer) en respectant ce relief, et chaque passage est ouvert par un col ou un gué sur des cellules réservées dès sa localisation. Les départs sont choisis sur la structure obtenue, puis les terrains sont affectés en une seule passe. Le même maillage produit le mesh 3D et les sources du navmesh.

Chaque phase reçoit un sous-flux PRNG dérivé du seed et ne dépend que des sorties des phases précédentes. Un échec après réparations locales rejoue **uniquement la phase concernée** avec son sous-flux suivant, dans une limite bornée, puis remonte à la phase précédente.

**Budget global déterministe de rejeux** (R14, R16) : un compteur `TotalRejeux` (toutes phases confondues, plafond **12**) et un compteur d'opérations simulé `Travail` (nombre de cellules/coins visités, plafond équivalent à ≈ 400 ms, sans horloge) sont tenus par le pipeline. Au dépassement de l'un des deux, ou à l'épuisement des rejeux de P3, le pipeline dérive `seedDerive = SplitMix64(seed ^ (k + 1))` avec `k` incrémenté (borné à **4**) et repart de P1. `MapData.Seed` conserve le seed d'origine, `MapData.Derivation = k` est stocké et inclus dans le hash ; tous les clients suivent la même cascade et convergent donc vers la même carte. Au-delà de k = 4, le seed est déclaré **invalide** (le lobby ne propose que des seeds validés par la campagne « Tester N seeds », dont l'objectif est un taux de dérivation nul sur 10 000 seeds).

```
seed ──► P0  PRNG et sous-flux                      → Rng[phase]
        P1  Points (Poisson-disc r = 14 m, 1400 m)  → Sites[≈7 000] + 64 points de bordure
        P2  Delaunay + Voronoi + Lloyd ×2           → GrapheCellules (CSR : coins, voisins, arêtes, aires)
        P3  Île (masque, composante principale,     → Terre[], Plage[], DistCote[] (terre et mer)
            articulations, marge de bord)             ; test ratio 26-38 % (rejeu ≤ 8)
        P4  Relief et hydrologie                    → HauteurCoin[], Crete[cellule], Flux[coin], Durete[arête], bassins
        P5  90 zones (germes par bassin, croissance,→ ZoneDeCellule[], Gz (graphe des zones)   ; rejeu ≤ 5
            équilibrage mou, compacité, articulations)
        P6  Groupes 3-4 (glouton + recherche locale → GroupeDeZone[], Gg (graphe des groupes)  ; rejeu ≤ 5 → P5
            + dissolution locale des orphelines)
        P7  Passages (éligibilité ≥ 45 m, arbre     → Passages[] (2-4 arêtes fines), CellulesPassage[]  ; rejeu ≤ 50 → P6
            borné, degrés cibles, localisation)
        P8  Matérialisation des frontières          → Drapeaux (Massif, Lac, Gue, Col), Rivieres[], Lacs[], Massifs[],
            (segments, rivières, massifs, cols,       hauteurs finales (pente limitée, quantifiées 0,05 m),
            gués, lacs, plafond 45 %, connexité,      SocketsMines/Peche, ZoneMontagne par massif
            pente, sockets)
        P9  Départs (6, critères structurels)       → ZonesDepart[6], GroupesDepart[6], scores d'équité ; rejeu ≤ 5 → P6
        P10 Terrains, ressources, ponts             → Zone.Terrain, Riveraine, EmplacementsPont[]      ; rejeu ≤ 2
        P11 Validation R1..R18 + hash               → MapData (contrat immuable, ≈ 500 Ko brut, ≈ 200 Ko compressé)
        P12 Construction 3D                         → chunks terrain, massifs, eau (+ texture DistanceRive), rivières,
                                                       bordures, décor
        P13 Navmesh (sources manuelles, bake async) → NavMeshData ; vérification R15
```

Les phases P0 à P11 sont du C# pur (aucune API moteur Unity ; `float2` et `math.*` proviennent de `com.unity.mathematics`, ajouté **explicitement** au manifest comme dépendance directe, et sont utilisés comme simples types valeur) et s'exécutent hors thread principal. P12 et P13 consomment `MapData` et sont rejouables à l'identique sans relancer la génération.

---

## 4. Détail de chaque étape

### P0 — PRNG, sous-flux, bruit

```csharp
/// PRNG déterministe xoshiro128** (état 4 × uint), dérivable par phase.
public struct Rng {
    uint s0, s1, s2, s3;
    public static Rng DepuisSeed(ulong seed) { /* SplitMix64 → 4 mots non nuls */ }
    public Rng Deriver(int idPhase) => DepuisSeed(SplitMix64(Etat64() ^ (0x9E3779B97F4A7C15UL * (ulong)(idPhase + 1))));
    public uint Suivant();                          // xoshiro128**
    public float Float01() => (Suivant() >> 8) * (1f / 16777216f);
    public int Entier(int min, int maxExclu) => min + (int)(Suivant() % (uint)(maxExclu - min));
}
/// Bruit de valeur 2D à hachage entier (xxhash32 sur (ix, iy, seed)), interpolation quintique en float.
/// fBm(p, octaves, freq, lacunarite 2, persistance 0.5). JAMAIS Mathf.PerlinNoise ni System.Random.
public static class Bruit { public static float Valeur(float2 p, uint seed); public static float Fbm(float2 p, int octaves, float freq, uint seed); }
/// Tables : Sinus (1024 entrées), Exp négatif (256 entrées sur [0, 8]), Atan2 (1024 entrées). Aucune libm dans les décisions.
/// Toute conversion float → int d'indexation est CLAMPÉE : index = min(max((int)(x * echelle), 0), N - 1) ;
/// un test EditMode vérifie qu'aucun NaN n'entre dans Tables.* ni Bruit.* (les conversions hors plage diffèrent entre x64 et ARM64).
```

Identifiants de phase : P1 = 1, P2 = 2, … Chaque rejeu d'une phase utilise `Rng.Deriver(idPhase * 100 + numeroRejeu)`. En cas de dérivation de seed (section 3), toute la cascade repart de `Rng.DepuisSeed(SplitMix64(seed ^ (k + 1)))`.

### P1 — Points

- Poisson-disc de Bridson : rayon `r = 14 m` (paramètre `RayonPoisson`, 16 m conservé pour comparaison en campagne), `k = 20` essais, grille d'accélération de pas `r / √2`, domaine 1400 × 1400 m. Premier point tiré au centre + jitter. Résultat attendu (densité Bridson ≈ 0,62 de l'empilement hexagonal) : **6 500 à 7 500 points**.
- 64 points de bordure fixes (16 par côté, à 8 m hors du domaine) pour fermer le diagramme ; ces cellules sont **toujours** mer.
- Coordonnées **quantifiées à 1/1024 m** dès le tirage (multiples entiers de 1/1024) : chaque coordonnée tient sur 21 bits signés, ce qui permet les prédicats entiers de P2.

### P2 — Delaunay, Voronoi, Lloyd

- Delaunay : **DelaunatorSharp** (core, MIT) vendoré, avec ses prédicats remplacés par des versions **entières** : coordonnées × 1024 en `long` ; `orient2d` exact en `long` (produits de 42 bits) ; `incircle` exact par décomposition en deux `long` (ou `decimal` 96 bits) — P2 ne dépend plus d'aucun arrondi flottant. **VoronatorSharp n'est pas utilisé** (il travaille en `float` et embarque un second triangulateur).
- Voronoi/CSR maison (≈ 150 lignes) : circoncentres calculés en `double` à partir des triangles Delaunator, clampés à la boîte, puis **quantifiés à 1/1024 m** en sortie ; centroïdes de polygones en `double` pour Lloyd.
- Lloyd × 2 : `site = centroïde du polygone` (re-quantifié) puis reconstruction. Deux passes suffisent (polygones réguliers, jamais hexagonaux).
- Structure finale en Structure-of-Arrays / CSR :

```csharp
public sealed class GrapheCellules {
    public int NbCellules, NbCoins, NbAretes;
    public float2[] Sites, Coins;
    public int[] CoinsDeCellule, DebutCoins;       // CSR, sens trigonométrique — SEULE table topologique sérialisée
    public int[] VoisinsDeCellule, DebutVoisins;   // dérivés (même ordre que les arêtes de la cellule)
    public int[] AretesDeCellule;                  // dérivé : index d'arête pour chaque voisin
    public int[] AreteCelluleA, AreteCelluleB, AreteCoinA, AreteCoinB; // dérivés : une arête = 2 cellules + 2 coins
    public int[] CoinsVoisins, DebutCoinsVoisins;  // dérivé : graphe des coins (pour l'hydrologie)
    public float[] Aire, LongueurArete;            // dérivés
    /// Reconstruit toutes les tables dérivées depuis CoinsDeCellule/DebutCoins par une routine entière déterministe
    /// (arêtes numérotées par (min(cellA, cellB), max(cellA, cellB)) croissant). Utilisée à la lecture de MapData.
    public void ReconstruireDerives();
}
```

Aucun objet par cellule, aucun `Dictionary` : toutes les recherches passent par des index.

### P3 — Île

```csharp
bool EstTerre(float2 p) {
    float2 c = p / 700f - 1f;                       // [-1, 1]²
    float r = math.length(c);
    float ang = Tables.Atan2(c.y, c.x);
    float radial = 0.62f + 0.12f * Tables.Sin(3 * ang + 1.7f) + 0.06f * Tables.Sin(7 * ang + 4.1f);
    float fbm = Bruit.Fbm(p, 4, 0.003f, seedIle);   // [-1, 1]
    return r < radial + 0.08f * fbm;                // rayon max 0,88 → marge nominale ≈ 84 m à la bordure
}
```

- Rayon moyen 0,62 dans [-1, 1]² : aire d'île attendue ≈ 31 % de la carte (≈ 610 000 m²), fenêtre d'acceptation **[0,26 ; 0,38]**.
- Classement des cellules par leur site ; **marge de bord** : toute cellule dont le site est à moins de 48 m du bord du domaine, ou adjacente à un point de bordure, est forcée mer (R1). Conservation de **la plus grande composante** terre (BFS 1er voisinage) ; îlots résiduels → mer ; poches de mer enclavées → terre (candidates lacs en P8).
- **Cellules d'articulation** du graphe terre (Tarjan) : si la suppression d'une cellule isole une composante de **moins de 6 cellules**, cette composante est convertie en mer (langue côtière inutilisable) ; si elle isole une composante de ≥ 6 cellules (isthme d'une cellule), la carte est rejetée immédiatement (rejeu P3) car aucun transfert ultérieur ne pourrait rendre les zones 2-connexes.
- Acceptation : ratio terre ∈ [0,26 ; 0,38] et aucune articulation résiduelle, sinon sous-flux suivant (≤ 8 essais ; en pratique 1-2). Après 8 essais : dérivation de seed (section 3).
- `DistCote[c]` : BFS multi-sources depuis les cellules terre bordant la mer (en pas de cellules, valeurs positives côté terre) **et** depuis les cellules mer bordant la terre (0, 1, 2, 3 côté mer, stockées négatives) ; `Plage` = terre avec `DistCote == 0`. Les valeurs côté mer servent au maillage de la couronne côtière (P12).

### P4 — Relief et hydrologie

1. **Élévation de base par coin** (pente douce vers la mer) : `h = 1,5 + 11 · (1 − ExpNeg(DistCoteCoin / 9)) + 1,5 · fbm2(p · 0,01)` ; plateau ≈ 12,5 m au centre.
2. **Champ de crêtes par cellule en lignes continues** (bruit « ridged ») : `Crete[c] = smoothstep(0,45 ; 0,75 ; 1 − |fbm3(site · 0,006)|)` ∈ [0, 1], calibré pour couvrir **20 à 30 % des cellules** en lignes fines et continues (mesuré en campagne ; réglage par `SeuilCreteBas/Haut`) ; ajout au relief : `h += 6 · Crete(moyenne des cellules du coin)` (les crêtes montent jusqu'à ≈ 18 m, pente moyenne toujours < 4°).
3. **Comblement des dépressions** sur le graphe des coins (priority-flood, file de priorité initialisée par les coins de mer) : `h[n] = max(h[n], h[c] + 0,02)` ; chaque coin possède ensuite un chemin descendant vers la mer. Les bassins comblés de **3 à 6 cellules** sont mémorisés comme **candidats lacs intérieurs** (les bassins plus grands sont comblés sans être candidats : un lac ne doit jamais avaler une zone).
4. **Accumulation de flux** : coins triés par hauteur décroissante ; chaque coin draine vers son voisin le plus bas ; `Flux[bas] += Flux[c]` (init 1). Une **rivière naturelle** = chaîne maximale de coins avec `Flux ≥ SeuilFlux` (défaut 40) ; on ne garde que les 4 à 6 rivières de plus fort flux à l'embouchure, les affluents fusionnent naturellement. Les **bassins versants** de ces rivières (et les zones entre crêtes fortes) définissent les **bassins d'agrégation** utilisés par P5.
5. **Dureté par arête fine** : `Durete[a] = 1` si l'arête (coinA → coinB) est un segment de rivière naturelle, sinon `moyenne(Crete[cellA], Crete[cellB])` ; `Durete = 1` aussi pour une arête touchant la mer.

Sorties : `HauteurCoin[]`, `Crete[]`, `Flux[]`, `Durete[]`, `RivieresNaturelles[]` (chaînes de coins), `BassinsCandidats[]`, `BassinDeCellule[]`.

### P5 — 90 zones

```csharp
int[] AgregerZones(GrapheCellules g, bool[] terre, float[] crete, float[] durete, int[] bassin, Rng rng) {
    // Germes répartis au prorata de l'aire des bassins : nbGermes[b] = round(aire[b] / aireCible), corrigé pour totaliser 90
    // (les bassins < 0,5 aireCible sont rattachés au bassin voisin de plus grande frontière commune)
    int[] germes = FarthestPointSamplingParBassin(g, terre, bassin, 90, rng); // 1er germe aléatoire, puis argmax de la distance min
    var zoneDe = Rempli(g.NbCellules, -1); float[] aire = new float[90];
    var file = new FilePrioriteMin<(float cle, int zone, int cell, int ordre)>();   // ordre = départage déterministe
    for (int z = 0; z < 90; z++) { zoneDe[germes[z]] = z; aire[z] = g.Aire[germes[z]]; PousserVoisins(z, germes[z]); }
    while (file.Count > 0) {
        var (cle, z, c, ordre) = file.Pop();
        if (zoneDe[c] != -1 || !terre[c]) continue;
        float cleCourante = Cle(z, c);                     // recalculée avec l'aire ACTUELLE de z
        if (cleCourante > cle * 1.10f) { file.Push((cleCourante, z, c, ordre)); continue; }   // clé périmée → on re-pousse
        zoneDe[c] = z; aire[z] += g.Aire[c]; PousserVoisins(z, c);
    }
    // clé d'une candidate c depuis la cellule source s de la zone z :
    //   cle = aire[z] * (1 + 2 * crete[c] + 4 * durete[arete(s, c)])   → la plus petite zone grandit d'abord,
    //   franchir une crête ou une rivière coûte cher → les frontières les épousent (mais restent franchissables)
    Equilibrer(g, zoneDe, aire, terre, crete, durete);
    CorrigerArticulations(g, zoneDe, aire);
    return zoneDe;
}
```

**Équilibrage** (≤ 400 itérations, convergence typique en 30-80 transferts) : tant que `max > 1,25 · médiane` ou `min < 0,75 · médiane`, la zone la plus grande cède une cellule de bord à sa voisine la plus petite. Un transfert est **refusé** si : la cellule est un point d'articulation de sa zone (BFS local) ; la zone cédante tomberait sous 4 cellules ; le rapport périmètre²/aire de l'une des deux zones dépasserait 28. La dureté est une contrainte **molle** : les transferts « mous » (arête franchie de `Durete ≤ 0,6`) sont toujours essayés d'abord, par dureté croissante ; un transfert à travers une arête de `Durete > 0,6` n'est autorisé que si **aucun** transfert mou n'est possible pour la zone hors tolérance ; la crête ou la rivière ainsi franchie devient alors un élément **intra-zone** (comme la croissance le fait déjà), ce qui évite l'échec systématique sur les bassins d'aire non multiple de l'aire de zone. Si aucun transfert n'est possible côté « plus grande », on tente côté « plus petite » en réception.

**Articulations** : construction de `Gz` (arête si ≥ 2 coins communs, soit une frontière réelle). Le **degré 1 est toléré** (zone en bout de péninsule, groupable avec sa seule voisine). Un point d'articulation de `Gz` (Tarjan) n'est traité que si l'une des composantes qu'il sépare compte **moins de 3 zones** (elle ne pourrait former un groupe qu'avec lui) : ≤ 10 transferts de cellules de bord vers une seconde voisine, sinon rejeu de P5 (≤ 5), sinon retour à P3.

Garde-fou de sortie : 90 zones, toutes connexes, aires ∈ ± 30 % (attendu ± 25 %), degré ≥ 1 dans `Gz`, aucune articulation isolant < 3 zones. **Après P5, aucune cellule ne change plus jamais de zone.**

### P6 — Groupes de 3-4 zones

1. **Décomposition** 3a + 4b = 90 tirée initialement parmi {(22, 6), (18, 9), (14, 12), (10, 15), (6, 18)} avec poids {1, 2, 3, 2, 1} → 24 à 28 groupes, défaut (14, 12) = 26 groupes. La décomposition n'est **pas** figée : la recherche locale et la réparation peuvent la faire évoluer librement, seule la contrainte tailles ∈ {3, 4} compte.
2. **Initialisation gloutonne** : zone non groupée ayant le moins de voisines libres d'abord (on ferme les impasses), taille cible prise dans la liste restante, extension par la voisine libre la plus compacte (somme des distances de centroïdes minimale).
3. **Réparation des orphelines par dissolution locale** : pour chaque zone orpheline (ou groupe de taille 1-2), on dissout les groupes voisins jusqu'à obtenir un pool connexe de **6 à 9 zones** (1 + 4 + 4 = 9 → 3 + 3 + 3 ; 1 + 3 + 4 = 8 → 4 + 4 ; 1 + 3 + 3 = 7 → 3 + 4 ; 2 + 4 = 6 → 3 + 3), puis on re-partitionne ce pool par **recherche exhaustive** (≤ 9 zones : < 3 000 partitions connexes en parts de 3-4) en minimisant l'énergie ci-dessous. Si aucun pool valide n'existe (orpheline entourée de mer et d'un seul groupe non dissoluble), rejeu de P6.
4. **Recherche locale** (recuit déterministe, 2 000 itérations, T de 60 à 1 en décroissance géométrique, `exp` par table) :

```csharp
float Energie(Partition p) =>
      1000f * NbGroupesTailleHors34(p) + 1000f * NbGroupesNonConnexes(p)
    + Somme(aretesInterGroupes, a => a.Longueur * (1f - a.Durete))            // frontière de groupe molle : pénalisée
    + 0.6f * Somme(aretesIntraGroupe, a => a.Longueur * a.DureteCrete)        // crête à l'intérieur d'un groupe : pénalisée
    + 2.0f * Somme(aretesIntraGroupe, a => a.LongueurRiviereNaturelle)        // rivière naturelle intra-groupe : fortement pénalisée
    + 40f * Somme(groupes, g => 1f - Compacite(g));
// mouvements : déplacer une zone frontalière vers un groupe voisin ; échanger deux zones frontalières ;
//   les transferts faisant passer une taille par 2 ou 5 sont AUTORISÉS (pénalité 1000, donc acceptés seulement à haute T)
// acceptation : dE < 0 ou rng.Float01() < Tables.ExpNeg(dE / T)
// arrêt : E < 1000 et aucune amélioration sur 300 itérations
```

`a.Durete` d'une arête de `Gz` = moyenne pondérée par la longueur des `Durete` des arêtes fines communes ; `a.DureteCrete` ne compte que la composante crête ; `a.LongueurRiviereNaturelle` = longueur cumulée des arêtes fines communes portant une rivière naturelle. Si E ≥ 1000 à la sortie : réparation par dissolution locale (2 tentatives), puis rejeu de P6 avec sous-flux suivant (≤ 5), puis retour à P5.

Sortie : `GroupeDeZone[]`, `Gg` (nœud = groupe, arête = liste des arêtes fines communes, avec longueur totale de chaîne et dureté).

### P7 — Passages

```csharp
// 0. Éligibilité : une arête de Gg n'est éligible au passage que si sa chaîne d'arêtes fines communes mesure ≥ 45 m
//    (les chaînes plus courtes restent des frontières bloquantes : aucun col plausible n'y tient).
//    Si le sous-graphe des arêtes éligibles n'est pas connexe → Rejouer(Phase.Groupes).
// 1. Arbre couvrant à degré borné (Kruskal sur les arêtes éligibles, poids aléatoires par arête, départage par id)
for (int essai = 0; essai < 50; essai++) {
    var uf = new UnionFind(nbGroupes); deg = zéros;
    foreach (var e in aretesEligibles.TrieesPar(e => (poids[essai][e.Id], e.Id)))
        if (uf.Trouver(e.A) != uf.Trouver(e.B) && deg[e.A] < 3 && deg[e.B] < 3) { Ajouter(e); uf.Unir(e.A, e.B); }
    if (uf.NbComposantes == 1) break;
    if (essai == 49) return Rejouer(Phase.Groupes);          // JAMAIS de degré 4
}
// 2. Degré cible par groupe : 1 (25 %), 2 (45 %), 3 (30 %) ; ajout d'arêtes éligibles non-arbre tant que les deux degrés < cible
// 3. Localisation : sur la chaîne de la frontière commune, on choisit le coin qui minimise (Crete moyenne + Flux normalisé)
//    parmi les coins situés à ≥ 14 m des deux extrémités ; le passage = suite MINIMALE de k arêtes fines consécutives
//    (k = 2..4) centrée sur ce coin dont la longueur cumulée ≥ 28 m → col plausible ou gué
// 4. CellulesPassage = cellules incidentes aux k arêtes de passage + cellules incidentes aux k + 1 coins du passage ;
//    ces cellules sont RÉSERVÉES : interdites à tout drapeau Massif/Lac et à tout lit de rivière non-gué (P8)
```

Un passage = `Passage { GroupeA, GroupeB, ZonesA[], ZonesB[], AretesFines[k], Coins[k + 1], CellulesPassage[], Nature (Col | Gue | BandeCotiere), Position, Largeur ≥ 28 m }`. Les zones touchées peuvent être multiples de chaque côté (le passage peut chevaucher deux zones d'un même groupe).

### P8 — Matérialisation des frontières naturelles

1. **Réservation** : les `CellulesPassage` de tous les passages et les arêtes qui leur sont incidentes sont marquées `Reserve` ; aucune étape suivante ne peut y poser un massif, un lac ou un lit de rivière non-gué.
2. **Chaînes et segments** : les arêtes fines inter-groupes sont assemblées en chaînes de coins (arêtes consécutives partageant un coin), puis **coupées** en segments (a) à chaque **jonction** (coin incident à ≥ 3 arêtes inter-groupes, c'est-à-dire là où ≥ 3 groupes se rencontrent) et (b) à chaque **passage** (les arêtes de passage n'appartiennent à aucun segment). Un segment sépare donc exactement deux groupes et n'a que deux extrémités (mer, jonction ou passage).
3. **Type par segment**, dans cet ordre :
   - le segment porte une rivière naturelle sur ≥ 0,5 de sa longueur → **Rivière naturelle** sur les arêtes effectivement porteuses de flux (largeur `w = 4 + 1,5 · √Flux` m, plafonnée à 14, sens = sens d'écoulement réel) ; les arêtes du segment **sans flux** sont typées comme un sous-segment indépendant par les règles suivantes, en démarrant au coin où la rivière quitte la frontière ;
   - les deux extrémités touchent la mer et longueur < 3 coins → **Bras de mer** (rivière large, dernier coin = coin de mer) ;
   - segments restants classés par `Crete` moyenne des cellules riveraines, **par rang** : les segments sont regroupés en chaînes de crête connexes (segments consécutifs à travers les jonctions) ; les chaînes sont triées par `Crete` moyenne décroissante et les **k premières deviennent des massifs**, `k` tiré dans **[6, 10]** (paramètre `NbMassifsMin/Max`) ; une chaîne n'est retenue comme massif que si sa `Crete` moyenne ≥ 0,15 (sinon k est réduit d'autant) ;
   - sinon → **Rivière prolongée** : Dijkstra sur les coins depuis le coin de jonction ou d'extrémité aval du segment jusqu'à la mer ou un lac, coût = `1 + 4 · max(0, montée)`, **coût infini** sur toute montée > 1,5 m, sur toute arête incidente à un coin de passage et sur toute arête d'une cellule réservée ; si aucun chemin en ≤ 40 coins ou si le plancher de hauteur (ci-dessous) est atteint avant la mer → terminaison en **lac** (2-4 cellules autour du coin terminal, jamais réservées, aplani au niveau min − 1,5 m).
   - **Continuité aux jonctions** : à chaque jonction, soit l'eau continue (un segment rivière aval unique, exutoire vers la mer ou un lac), soit un massif d'au moins 1 cellule occupe la jonction ; un segment rivière ne peut pas se terminer « à sec » à une jonction.
4. **Hauteurs des rivières** (naturelles et prolongées) : hauteurs des coins forcées **monotones décroissantes** vers l'aval avec un pas de 0,05 m (`h[i] = min(h[i], h[i−1] − 0,05)`) et un **plancher de 1,5 m** hors des 3 derniers coins avant la mer ; si le plancher est atteint avant la mer ou un lac, la rivière se termine en lac à cet endroit. Profils (par rapport à la hauteur `h` du coin) : **lit = h − 2,0 ; eau = h − 1,0 ; berge (sites adjacents) = h − 0,8** (l'eau reste 0,2 m sous la berge, sans z-fighting) ; largeur du ruban ≤ 0,8 × (2 × distance coin-site). Sur les 3 derniers coins avant la mer : interpolation linéaire eau → 0, lit → −1,5, berge → 0,5 ; pour un lac exutoire, eau → niveau du lac. Un **gué** conserve le ruban : lit = h − 0,9, eau = h − 0,8 (à hauteur de berge, donc toujours ≥ 0,6 m au-dessus de la mer), 3-5 galets, aucun volume navmesh.
5. **Gués intra-groupe** : tout segment de rivière (naturelle ou prolongée) situé **à l'intérieur d'un groupe** conserve sa largeur réelle et reçoit un gué **tous les 3-4 coins** (le premier au coin d'entrée dans le groupe) ; les zones riveraines de ces segments restent `Riveraine = true` (poisson). La règle « ce qui bloque est une frontière » reste vraie : seules les rivières inter-groupes hors gué bloquent.
6. **Massifs** : bande de cellules de chaque côté de la chaîne, calculée sur les cellules non réservées uniquement, **asymétrique** : 2 cellules du côté de la **zone Montagne du massif**, 1 cellule de l'autre côté. La zone Montagne d'un massif = parmi les zones touchées par la chaîne, celle qui partage la plus grande longueur de chaîne (départage : plus grande réserve constructible, puis id) ; deux massifs peuvent désigner la même zone. Chaque cellule reste dans **sa** zone. **Prolongement** de 1-2 cellules en s'effilant (1 cellule, puis rien) uniquement aux extrémités **mer ou jonction** ; **aucun prolongement** à une extrémité qui borde un passage. Hauteurs de crête continue : hauteur des **coins** massif partagée entre cellules massif adjacentes = `h + 6 + 8 · (nbCellulesMassifAdjacentes / 3)` ; hauteur du **centre** = moyenne des coins + 4 + hash(id) · 4, plafonnée à `h + 20` ; la bande donne ≈ 30-40 m de base pour ≈ 20 m de haut (pente ≈ 50°, faces ≤ 60° vérifiées). Neige à partir de `hBase + 14 m`.
7. **Passages** : col = massif interrompu, hauteurs interpolées pour garantir < 40° ; gué = rivière conservée au profil « gué » et aucun volume navmesh ; bande côtière = plage continue. **Largeur réelle** du col = distance entre les deux cellules massif (ou lac) les plus proches de part et d'autre : si < 24 m, on retire la cellule massif la plus proche du col (répété jusqu'à ≥ 24 m).
8. **Lacs supplémentaires** : 1 à 2 lacs intérieurs choisis parmi `BassinsCandidats` (3-6 cellules, loin de la côte `DistCote ≥ 4`) + jusqu'à 2 lacs aux jonctions ≥ 3 groupes loin de la côte. Un candidat est rejeté s'il touche une cellule réservée, s'il représente > 25 % des cellules d'une zone touchée, ou s'il déconnecte les cellules praticables d'un groupe (BFS de l'étape 10).
9. **Plafond constructible** : pour chaque zone, part de cellules **Massif + Lac** ≤ 45 % (les rivières ne consomment pas de cellules ; elles imposent une marge d'implantation de w/2 + 1 m vérifiée sur l'empreinte du bâtiment au moment de la construction). En cas de dépassement, **sans jamais changer l'appartenance d'une cellule** : amincissement de la bande à 1 cellule sur cette zone, retrait des prolongements, puis plafonnement à ⌊0,45 · nbCellules⌋ en retirant les cellules Massif les plus éloignées de la chaîne. Si la chaîne ne peut plus être bloquée avec ≥ 1 cellule par arête, le segment est **retypé en rivière prolongée** (aucune cellule consommée). Sinon rejeu de P8 (≤ 3) puis retour à P6.
10. **Connexité des cellules praticables** : BFS sur les cellules praticables (non massif, non lac ; arêtes non-rivière ou gué) **par zone** puis **par groupe** ; on exige une composante unique par groupe contenant toutes les cellules praticables de ses zones. En cas d'échec : suppression du prolongement fautif, amincissement de la bande à 1 cellule, ajout d'un gué sur la rivière prolongée au point de coupure ; sinon rejeu de P8. Mesure remontée au panneau de validation (R10).
11. **Limitation de pente et quantification** : deux passes ; si `|Δh| > tan(40°) · distance` entre centre et coin hors massif, on **abaisse le site adjacent** (jamais on ne relève un coin de rivière) ; faces de massif ≤ 60° ; puis toutes les hauteurs sont quantifiées à **0,05 m** (`short × 20`). Après cette étape, hauteurs et mesh coïncident exactement.
12. **Sockets** (géométriques, avant les départs) : **sockets de mines** = cellules constructibles de la **zone Montagne** du massif adjacentes à une cellule massif, regroupées en 2-4 sockets par massif, espacées ≥ 25 m ; ≥ 1 socket par massif garanti (sinon la cellule pied la plus proche est forcée constructible). **Sockets de pêche** = cellules constructibles à ≤ 10 m d'une eau (mer, lac, rivière y compris segments à gués), regroupées 1-3 par zone, espacées ≥ 25 m. `Riveraine = true` si la zone possède ≥ 1 arête fine bordant la mer, un lac ou une rivière.
13. **Assertions** : chaque arête inter-groupes hors passage a **au moins une cellule incidente NotWalkable** (massif ou lac) **ou porte un volume rivière/mer** ; aucune cellule réservée n'est massif/lac/lit non-gué ; aucune boîte de rivière n'intersecte le rectangle d'un passage ; chaque groupe a une composante praticable unique.

### P9 — Départs

1. **Filtre de candidats** (critères structurels, terrains pas encore affectés) : tous les groupes de taille 3 ou 4 dont le degré dans `Gg` ∈ **[2, 3]** (jamais de repli à 1), **sans lac**, et possédant au moins une zone « de départ » : ≥ 85 % de cellules constructibles, sans lac, part massif < 10 %, praticable en une composante, non désignée zone Montagne d'un massif ; la zone de départ = la plus centrale du groupe parmi celles qui satisfont ces critères. Classement par (taille décroissante, degré, part massif), **sans coupure à 6**.
2. **Sélection** « point le plus éloigné » sur tout l'ensemble des candidats, distance = `0,6 · distGraphe(Gg) normalisée + 0,4 · distEuclidienne normalisée` ; contraintes : distGraphe ≥ 3 passages et distEuclidienne ≥ `distMin = 0,75 · √(aireÎle / 6)` (≈ 240 m) entre deux départs ; **repli** à distGraphe ≥ 2 si moins de 6 départs sont trouvés, puis rejeu de P9 (≤ 5, change le premier tirage), puis retour à P6. La distance minimale **mesurée** est affichée, pas seulement le seuil.
3. **Score structurel** = `1 · aireConstructibleGroupe (normalisée) + 2 · nbSocketsMines + 1 · nbSocketsPeche + 1 · nbZonesDuGroupeAUnPassage + degréNormalisé` (groupe + groupes à 1 passage pour les sockets) ; écart entre les 6 ≤ 20 %, sinon échange du pire départ avec le candidat suivant (≤ 10 essais), puis rejeu de P9 (≤ 5).
4. Ordre des départs fixé par seed : pour k joueurs (2 ≤ k ≤ 5), on prend les k premiers de l'ordre de sélection (les plus éloignés entre eux).

Sortie : `ZonesDepart[6]`, `GroupesDepart[6]`, scores.

### P10 — Terrains, ressources, ponts

Statistiques par zone : élévation moyenne, humidité (`1 − min(1, distEau / 150 m)` avec distEau = distance à la mer, un lac ou une rivière), part de plage, part de massif, bruit d'aridité.

| Terrain | Quota | Score dominant | Ressource |
|---|---|---|---|
| Montagne | = nombre de zones désignées par les massifs (6-10) | forcé : zone Montagne de chaque massif | Minerai (mines sur sockets) |
| Côte/plage | ≈ 11 (± 2) | part plage ≥ 0,5 | Aucune ressource propre (poisson via `Riveraine`) |
| Désert | **maximum 6** | humidité minimale + bruit d'aridité | Aucune |
| Marais | **maximum 6** | élévation < 3,5 m, humidité > 0,7 | Aucune |
| Plaine fertile | reste × 18/58 (≈ 18) | élévation basse, humidité 0,45-0,7 | Nourriture |
| Forêt | reste × 18/58 (≈ 18) | humidité > 0,6, élévation moyenne | Bois |
| Colline | reste × 12/58 (≈ 12) | élévation haute, `Crete` moyen | Pierre |
| Plaine argileuse | reste × 10/58 (≈ 10) | élévation basse, adjacente rivière/lac | Argile |

« Reste » = 90 − Montagne − Côte − Désert − Marais, arrondi pour totaliser 90 (le plus grand reste absorbe l'écart).

Affectation en **une seule passe** : d'abord les zones Montagne (forcées) et la **normalisation des groupes de départ** (zone de départ = Plaine fertile ; les autres zones du groupe reçoivent Forêt, puis Colline ou Plaine argileuse, la 4e reste libre) ; puis couples (zone, type) triés par score décroissant, bonus **+ 0,15 si une zone voisine a déjà ce terrain** (biomes contigus), sous quotas. Contraintes intégrées à l'affectation : désert/marais **interdits** dans les zones de départ et leurs voisines directes (pas tout le groupe) ; désert/marais jamais adjacents à plus d'une autre zone sans ressource. Boucle bornée : échanges locaux ≤ 20, puis rejeu de P10 ≤ 2, puis **acceptation avec quota désert/marais réduit** (les quotas de ces deux terrains sont des maxima, jamais des minima).

- **Emplacements de ponts** : pour chaque frontière rivière inter-groupes **sans passage**, le segment de rivière le plus droit (angle entre segments consécutifs minimal) de largeur ≤ 16 m et de berges de pente < 25° ; 1 au plus par frontière ; stocké dans `MapData`.

### P11 — Validation et hash

Toutes les assertions de la section 5 sont réévaluées (R2, R3 et `Gz` sont recontrôlés ici bien que P8 ne change plus les zones). `MapData.Hash` = FNV-1a 64 de la **partie discrète seule** : `Derivation`, `ZoneDeCellule`, `GroupeDeZone`, `Drapeaux`, terrains, passages, gués, ponts, départs, chaînes, calculé sur la forme canonique après `ReconstruireDerives()`. Un second hash `HashHauteurs` (hauteurs quantifiées à 0,05 m) est publié à titre **informatif, non bloquant** : la partie discrète suffit à garantir la même partie. `MapData.VersionGenerateur` est incrémentée à chaque changement d'algorithme.

---

## 5. Garantie règle par règle

| Règle | Mécanisme | Vérification (éditeur) | En cas d'échec |
|---|---|---|---|
| R1 île unique | Conservation de la plus grande composante ; îlots → mer ; marge de bord 48 m et exclusion des cellules adjacentes aux points de bordure ; articulations traitées dès P3 ; ratio terre testé | `NbComposantesTerre == 1`, ratio ∈ [0,26 ; 0,38], 0 cellule terre à < 48 m du bord, 0 articulation | Sous-flux P3 suivant (≤ 8) → dérivation de seed |
| R2 90 zones | 90 germes ; croissance exhaustive couvre toute la terre ; l'équilibrage n'a jamais le droit de vider une zone (≥ 4 cellules) ; aucun changement de zone après P5 | `zones.Length == 90 && toutes connexes` (recontrôlé en P11) | Rejeu P5 (≤ 5) → P3 |
| R3 ± 30 % | Germes au prorata des bassins, croissance « plus petite d'abord » (clé recalculée) puis transferts jusqu'à ± 25 % avec dureté molle | min/méd/max, histogramme (recontrôlé en P11) | Rejeu P5 |
| R4 organiques | Poisson-disc + Lloyd × 2 (jamais de grille) ; bruitage optionnel des arêtes à l'affichage | indice de compacité aire/périmètre² moyen ∈ [0,045 ; 0,075] | Réglage `RayonPoisson` / `SubdivisionsArete` |
| R5 groupes 3-4 | Décomposition libre, termes × 1000 dans l'énergie, dissolution locale + re-partition exhaustive des orphelines | tailles ∈ {3, 4}, BFS par groupe | Rejeu P6 (≤ 5) → P5 |
| R6 frontières naturelles | Croissance pondérée par crêtes/rivières ; énergie de groupement pénalisant les frontières molles et les rivières intra-groupe ; matérialisation par segment de **toutes** les arêtes inter-groupes hors passage | 100 % des arêtes couvertes ; dureté moyenne avant matérialisation ≥ 0,5 ; nombre de massifs ∈ [6, 10] ; % de frontières « sans relief sous-jacent » | Rejeu P8 (≤ 3) → P6 |
| R7 passages 1-3, connexe, praticables | Éligibilité ≥ 45 m, connexité du sous-graphe éligible, Kruskal borné + ajouts bornés ; passage 2-4 arêtes ≥ 28 m ; cellules réservées ; largeur réelle ≥ 24 m ; adoucissement des cols < 40° | degrés ∈ [1, 3], BFS `Gg`, largeur réelle par passage, raycast local ouvert (section 7.7) | Rejeu poids (≤ 50) → P6 ; retrait de cellule massif si col trop étroit |
| R8 ponts | Emplacement calculé en P10, activé par `NavMeshLink` | liste des emplacements, `SamplePosition` sur chaque extrémité | Aucun emplacement possible = pas de pont sur cette frontière (autorisé) |
| R9 un terrain par zone | Attribut porté par la zone, jamais par la cellule | — | — |
| R10 englobés, ≥ 55 % constructible, connexité, mines | Cellules massif/lac membres de leur zone ; plafond 45 % Massif + Lac sans changement de zone ; BFS des cellules praticables par zone et par groupe ; sockets par massif dans sa zone Montagne | part constructible min, composantes praticables par groupe (= 1), nb sockets par massif | Amincissement, retrait des prolongements, gué de coupure, retypage en rivière, rejeu P8 |
| R11 ressources, poisson | Table terrain → ressource ; `Riveraine` géométrique (y compris segments à gués) | liste des zones riveraines | — |
| R12 quotas, désert/marais, massif → Montagne | Affectation en une passe sous quotas (désert/marais maxima) + échanges locaux bornés | distribution vs quotas | Échanges (≤ 20), rejeu P10 (≤ 2), acceptation à quota réduit |
| R13 départs | Filtre structurel, farthest-point sur tous les candidats, seuil relatif, degré ∈ [2, 3], score structurel ± 20 %, ordre fixe | distance min mesurée vs `distMin`, sauts min, écart de score, écart-type des distances au voisin ≤ 15 % | Échanges (≤ 10), repli ≥ 2 sauts, rejeu P9 (≤ 5) → P6 |
| R14 déterminisme | Section 9 (spike J1, flags FMA, prédicats entiers, hash discret, budget de rejeux, dérivation bornée) | hash affiché, 30 seeds de référence, taux de dérivation | Refus en lobby + envoi du `MapData` compressé |
| R15 navmesh | Sources exactes (ModifierBox, mesh de lac), test local par arête inter-groupes + chemins globaux dans l'union des groupes | liste rouge des arêtes/passages non conformes | Correction du massif/col fautif, rebake ; bug de génération à corriger |
| R16 perf | Section 9 | temps par phase, P95 sur 50 seeds, temps de bake mesuré sur appareil | Bake par tuiles, voxel 0,5 → 0,66 en dernier recours |
| R17 relief léger | Formule d'élévation, limitation de pente 40° hors massifs en P8 (avant hash), faces massif ≤ 60° | pente moyenne, 0 triangle > 40° hors massif, 0 face massif > 60° | Abaissement de site supplémentaire |
| R18 bordures | Mesh ruban généré depuis les chaînes de coins de chaque zone, projeté sur le relief, filtré par propriétaire | visuel | — |

---

## 6. Modèle de données

Tous les noms sont en français ; `MapData` est un POCO à tableaux plats (aucune référence d'objet), sérialisable en binaire (`BinaryWriter`, little-endian, ≈ 500 Ko brut pour ≈ 7 000 cellules, 14 000 coins et 21 000 arêtes ; ≈ 200 Ko après compression LZ4 ou Deflate pour l'envoi de secours) et en JSON (`JsonUtility`) pour inspection. Les tableaux **dérivables** (`VoisinsDeCellule`, `DebutVoisins`, `AretesDeCellule`, `AreteCelluleA/B`, `AreteCoinA/B`, `LongueurArete`, graphe des coins) ne sont **pas** sérialisés : ils sont reconstruits à la lecture par `ReconstruireDerives()` (routine entière déterministe) et le hash est calculé sur la forme canonique après reconstruction.

```csharp
public enum TypeTerrain : byte { PlaineFertile, PlaineArgileuse, Foret, Colline, Montagne, Desert, Marais, Cote }
public enum TypeRessource : byte { Aucune, Nourriture, Argile, Bois, Pierre, Minerai }
public enum TypeFrontiere : byte { Libre, Massif, Riviere, BrasDeMer, Lac, Mer, Col, Gue, BandeCotiere }
[Flags] public enum DrapeauxCellule : ushort { Terre = 1, Plage = 2, Massif = 4, PiedMassif = 8, Lac = 16, RiveLac = 32,
    NonConstructible = 64, SocketMine = 128, SocketPeche = 256, Reserve = 512 /* cellule de passage */ }

[CreateAssetMenu(menuName = "Map Heroic/Définition de terrain")]
public sealed class DefinitionTerrain : ScriptableObject {
    public TypeTerrain Type; public string Nom;
    public Color Couleur; [Range(0, 0.1f)] public float VariationTeinte = 0.04f;
    public TypeRessource Ressource; public bool Constructible = true;
    public int QuotaCible, QuotaTolerance = 2; public bool QuotaEstMaximum;   // vrai pour Désert et Marais
    public int AireNavMesh;                 // index d'aire NavMesh (coût : marais 1,6, forêt 1,2, autres 1)
    public GameObject[] PrefabsDecor; [Range(0, 1)] public float DensiteDecor;
    public AnimationCurve ConvenanceElevation, ConvenanceHumidite; public float BonusPlage, BonusMassif;
}

[CreateAssetMenu(menuName = "Map Heroic/Paramètres de génération")]
public sealed class ParametresGeneration : ScriptableObject {
    public float TailleCarte = 1400; public float RayonPoisson = 14; public int IterationsLloyd = 2;
    public float RatioTerreMin = 0.26f, RatioTerreMax = 0.38f; public float MargeBord = 48f;
    public int NbZones = 90; public float ToleranceAireInterne = 0.25f, ToleranceAireRegle = 0.30f;
    public float CompaciteMax = 28f; public int NbJoueursMax = 6;
    public float SeuilFlux = 40, SeuilCreteBas = 0.45f, SeuilCreteHaut = 0.75f; public int NbMassifsMin = 6, NbMassifsMax = 10;
    public float LongueurMinChainePassage = 45, LargeurPassage = 28, LargeurColMin = 24, ProfondeurRiviere = 2f, PlancherRiviere = 1.5f;
    public float PartNonConstructibleMax = 0.45f; public int SubdivisionsArete = 1;   // 2 = arêtes bruitées (décision J8)
    public int LacCellulesMin = 3, LacCellulesMax = 6; public float PartLacMaxParZone = 0.25f;
    public int RejeuxTotalMax = 12, DerivationSeedMax = 4;
    public DefinitionTerrain[] Terrains;
    public ParametresAgentNavMesh Agent;    // rayon 0.6, hauteur 2, pente 40, marche 0.6, voxel 0.5, tuile 64
}

[Serializable] public sealed class MapData {
    public ulong Seed; public int Derivation; public int VersionGenerateur; public ulong Hash, HashHauteurs;
    public int NbCellules, NbCoins;
    public float2[] Sites, Coins;                      // quantifiés à 1/1024 m
    public int[] CoinsDeCellule, DebutCoins;           // seule topologie sérialisée ; le reste est reconstruit
    public short[] HauteurCoinQ, HauteurSiteQ;         // hauteurs finales × 20 (pas de 0,05 m), pente déjà limitée
    public ushort[] Drapeaux;                          // DrapeauxCellule
    public short[] ZoneDeCellule;                      // -1 = mer
    public sbyte[] DistCote;                           // ≥ 0 côté terre, < 0 côté mer (couronne côtière)
    public ZoneData[] Zones; public GroupeData[] Groupes; public AreteGroupesData[] AretesGroupes;
    public PassageData[] Passages; public RiviereData[] Rivieres;
    public LacData[] Lacs; public MassifData[] Massifs; public EmplacementPontData[] Ponts;
    public short[] ZonesDepart, GroupesDepart;         // taille 6, ordre fixe par seed
}
[Serializable] public struct ZoneData { public short Id, Groupe; public TypeTerrain Terrain; public bool Riveraine;
    public float Aire; public float2 Centroide; public int[] Cellules, Voisines, ChaineContour; public int[] SocketsMines, SocketsPeche; }
[Serializable] public struct GroupeData { public short Id; public short[] Zones, Passages, GroupesVoisins; public short Depart; /* -1 */ }
[Serializable] public struct AreteGroupesData { public short A, B; public bool Passage; public float Durete; public SegmentFrontiereData[] Segments; }
[Serializable] public struct SegmentFrontiereData { public TypeFrontiere Type; public int[] ChaineCoins; }
[Serializable] public struct PassageData { public short Id, GroupeA, GroupeB; public short[] ZonesA, ZonesB; public int[] AretesFines, Coins, CellulesPassage;
    public TypeFrontiere Nature; public float2 Position; public float Largeur; }
[Serializable] public struct RiviereData { public int[] Coins; public float[] Largeur; public bool[] Gue; public bool Naturelle; public short LacExutoire; }
[Serializable] public struct LacData { public int[] Cellules; public float Niveau; }
[Serializable] public struct MassifData { public int[] Cellules; public short[] ZonesTouchees; public short ZoneMontagne; }
[Serializable] public struct EmplacementPontData { public short GroupeA, GroupeB; public int CoinDebut, CoinFin; public float Largeur; }
```

Runtime : l'état de partie (propriétaire par zone, ponts construits) vit dans une structure séparée `EtatCarte { short[] ProprietaireZone; bool[] PontConstruit; }`, jamais dans `MapData`. En multijoueur, l'hôte publie `(Seed, Derivation, VersionGenerateur, Hash)` ; chaque client régénère et compare. Un `MapAsset : ScriptableObject { MapData Donnees; }` permet de figer des cartes de test ou de campagne.

---

## 7. Génération 3D et navmesh

### 7.1 Mesh terrain

- **Éventail par cellule** : 1 sommet centre (`HauteurSite`) + n sommets coins (`HauteurCoin`), partagés **à l'intérieur de la cellule uniquement** (une cellule = une couleur). Seules les cellules **terre** et la **première couronne de mer** (`DistCote ≥ −2`, fond marin visible en hauts-fonds) sont maillées ; le reste de la mer est fermé par un quad de fond à −6 m. ≈ 2 700 cellules maillées × 6 triangles ≈ **16 k triangles, ≈ 19 k sommets** avec `SubdivisionsArete = 1` (défaut) ; ≈ 32 k triangles, ≈ 35 k sommets avec `SubdivisionsArete = 2` (12 triangles et 13 sommets par cellule).
- **Flat shading** par dérivées écran : Shader Graph URP cible **Universal/Lit** avec `Fragment Normal Space = World`, Normal = `Normalize(Cross(DDY(Position World), DDX(Position World)))`, Base Color = Vertex Color.rgb, Smoothness 0, Specular désactivé, Metallic 0, « Additional Lights » désactivées dans l'asset URP Mobile ; aucune duplication de sommets par triangle, aucun `RecalculateNormals`. Variante bas de gamme : cible **Unlit** + Custom Function HLSL (`GetMainLight`, Lambert enveloppé, ombre de la main light, ambiante SH). (Shader Graph URP n'a pas de cible « Simple Lit ».) Un seul matériau terrain, couleur par vertex.
- **Couleur** (`Color32`) : couleur du terrain de la zone × (1 + variation hachée par cellule ± 4 %) ; plage → sable ; massif → gris roche (neige ≥ hBase + 14 m) ; pied de massif → terrain assombri de 10 % ; canal alpha = occlusion ambiante par vertex (creux de vallée, pied de massif). Le sable mouillé de la rive est lu dans la texture `DistanceRive` (section 7.3), pas dans la couleur de vertex.
- **Arêtes bruitées** (option, `SubdivisionsArete = 2`) : chaque arête Voronoi est subdivisée, les points intermédiaires déplacés de ± 12 % de la longueur par hachage de l'id d'arête, **contraints au quadrilatère (site A, coin 1, site B, coin 2)** → jamais d'auto-intersection et les deux cellules partagent la même courbe. Décision définitive à la recette visuelle du jalon J8 ; le critère de triangles J9 (≤ 40 k) couvre les deux options.
- **Chunks** : 5 × 5 de 280 m, seuls les chunks contenant des cellules maillées sont créés (≈ 16-20 meshes terrain, `IndexFormat.UInt16`, ≈ 1 200-2 300 sommets par chunk) ; remplissage par `Mesh.SetVertexBufferData` depuis des `NativeArray`.
- **Pente** : la limitation de pente 40° hors massifs a déjà été appliquée en P8 (avant quantification et hash) ; P12 n'altère **aucune** hauteur — `MapData` et mesh coïncident.
- **Sélection** : raycast sur un plan puis recherche de cellule par grille spatiale (pas de `MeshCollider`).

### 7.2 Massifs

Cellules massif extrudées en **crête continue** : les coins massif sont partagés entre cellules massif adjacentes (hauteur `h + 6 + 8 · (nbCellulesMassifAdjacentes / 3)`), le centre à `moyenne des coins + 4 + hash(id) · 4` (plafond `h + 20`), soit ≈ 20 m de haut sur ≈ 30-40 m de base (pente ≈ 50°, faces ≤ 60° contrôlées) — un relief localisé lisible, pas des aiguilles cellule par cellule. Écrits dans un sous-mesh par chunk (`Massifs_XY`) rendu avec le même matériau ; neige (blanc cassé) sur les faces dont le centroïde est ≥ hBase + 14 m. Props : rochers KayKit Forest sur les cellules pied et les flancs bas (1-2 par cellule) ; un prop « montagne » de pack n'est ajouté sur la ligne de crête (1 pour 3 cellules) que si l'inventaire du jalon J12 en confirme un à silhouette compatible (Quaternius), jamais les montagnes à empreinte hexagonale de KayKit Hexagon. Les cellules « pied » de la zone Montagne portent les sockets de mines (marqueurs visibles).

### 7.3 Eau, rivières, lacs, plages

- **Texture `DistanceRive`** (P12, ≈ 2 ms CPU) : R8 512 × 512 en coordonnées monde XZ, distance signée à la polyligne de côte + contours de lacs + axes de rivières, calculée par transformée de distance sur CPU depuis `MapData` ; échantillonnée par position monde dans le shader d'eau (écume, teinte peu profonde) et dans le shader terrain (sable mouillé). Un seul mécanisme pour mer, lacs et rivières ; 256 Ko.
- **Shader d'eau maison** (Shader Graph, cible Universal/Unlit — ou Lit smoothness 0,9 pour un reflet spéculaire simple —, Surface Transparent, ZWrite Off) : couleur = `lerp(Profond, PeuProfond, masqueRive)` ; ondulations = 2 textures de bruit 128² (voronoï et valeur) en défilement UV monde à 2 vitesses, additionnées ; écume = `step(masqueRive, seuil animé par sinus) × bruit` ; **aucune lecture** de `_CameraDepthTexture` ni `_CameraOpaqueTexture`, aucune réfraction. Compatible avec l'asset URP Mobile (depth/opaque désactivées).
- **Mer** : quad 1600 × 1600 à y = 0 ; fond marin : cellules de la couronne côtière à −1,5 m (hauts-fonds clairs) puis −4 m, quad de fond à −6 m au-delà ; plage entre 0,5 et 2 m.
- **Lacs** : cellules aplaties à `niveau − 1,5 m`, polygone d'eau au `niveau` (triangulation en éventail depuis le centroïde) ; ce polygone sert aussi de source navmesh NotWalkable.
- **Rivières** : ruban (strip) de largeur `w` le long des coins, jointures en biseau ; profils définis en P8 : lit = h − 2,0, eau = h − 1,0, berge = h − 0,8 ; embouchure interpolée sur 3 coins (eau → 0, lit → −1,5) ; gués : ruban conservé, lit = h − 0,9, eau = h − 0,8, 3-5 galets.
- **Ponts** : tablier **généré** (solution primaire, aucun pack retenu n'en fournit) : quad de longueur `w + 2 × 2 m` d'assise × largeur 6 m (cohérent avec `NavMeshLink.width = 6`), 2 poutres latérales, piquets tous les 3 m (Resource Bits « wood » ou cylindres générés), couleur de vertex bois de la palette, garde-corps teinté couleur joueur ; état « chantier » = piquets sans tablier. Posé sur `EmplacementPontData`. Si le KayKit Medieval Builder Pack (CC0) contient un pont réutilisable à l'import, il peut remplacer le tablier ; sinon le point est clos.

### 7.4 Bordures de pays (R18)

Ruban de 1,2 m généré depuis `ChaineContour` de chaque zone ; **chaque sommet du ruban est projeté sur la hauteur du terrain** (interpolation dans l'éventail de la cellule) + 0,08 m, rendu avec `Depth Offset` (nœud Shader Graph), file Transparent + 10, ZWrite Off, matériau unlit couleur joueur, animé (défilement UV) — le ruban reste visible sur les flancs de massif et près des cols. Pas de Decal Projector URP (coûteux sur mobile). Un segment n'est émis que si les deux zones qu'il sépare n'ont pas le même propriétaire (ou si l'autre côté est la mer). Régénéré par zone lors d'un changement de propriétaire (≈ 30 segments, négligeable). Contour de groupe plus épais (1,8 m) disponible en calque optionnel.

### 7.5 Décors

Placement par cellule constructible selon `DensiteDecor` du terrain — **forêt 2-3 arbres par cellule** (variantes KayKit à faible poly, ≤ 150 triangles), colline rochers, désert cactus, marais arbres morts + roseaux — positions par hachage de l'id de cellule. Budget vérifiable : **≤ 250 k triangles de décor visibles et ≤ 1 200 instances par vue** à zoom maximal (160 m, toute l'île visible) ; ≈ 3 000-4 000 instances au total sur la carte. Rendu : combinaison par (chunk, prefab) en un seul mesh statique (`CombineMeshes` en P12, 1 draw call par prefab et par chunk, culling par frustum au niveau du chunk) ; à zoom ≥ 120 m, les arbres individuels d'une cellule sont remplacés par un « bosquet » combiné par cellule. Les props dynamiques (marqueurs de sockets, chantiers) passent par `Graphics.RenderMeshInstanced` en lots ≤ 1 023 par (mesh, matériau), matériau props avec GPU Instancing activé. Jamais sur un socket, jamais à moins de 8 m d'un col ou d'un gué, jamais sur une cellule massif/lac/réservée ni dans la marge d'une rivière. Bosquets et rochers isolés hors frontières (probabilité 5 % par cellule constructible) pour casser la lecture « obstacle = frontière ». Interdiction de mélanger deux familles d'arbres dans une même zone.

### 7.6 Caméra

Rig 3/4 plongeant (55°), zoom 40-160 m, pan clampé à l'île, Input System (pinch + drag sur mobile, molette + clic droit sur PC).

### 7.7 Navmesh runtime

```csharp
// Sources construites manuellement, jamais de scan de hiérarchie
var sources = new List<NavMeshBuildSource>();
foreach (chunk) {
    foreach (aire in {Walkable, Foret, Marais}) sources.Add(SourceMesh(chunk.MeshNavParAire[aire], area: aire)); // meshes de navigation (non rendus), groupés par aire
    sources.Add(SourceMesh(chunk.MeshMassifs, area: NotWalkable));
}
// Rivières : ModifierBox (NavMeshBuildSourceShape.ModifierBox, area = 1 = Not Walkable, toujours prioritaire) par segment non gué :
//   size = (longueurSegment + 2, 8, w + 1), transform = TRS(centre du segment à la hauteur du lit, rotation du segment)
//   → force l'aire de tout navmesh dans son volume sans dépendre de la géométrie du lit ; recouvrement 1 m entre segments
foreach (segment de rivière non gué) sources.Add(SourceModifierBox(segment));
// Lacs : polygone d'eau du lac (déjà généré pour le rendu, avec 1 m de recouvrement) en source Mesh NotWalkable — jamais d'AABB,
//   qui bloquerait la rive, les sockets de pêche et les cols voisins
foreach (lac) sources.Add(SourceMesh(lac.MeshEau, area: NotWalkable));
// Mer : ModifierBox de y = −8 à y = +0,3 sur toute la carte (les gués sont à ≥ 0,6 m, les plages à ≥ 0,5 m)
sources.Add(SourceModifierBox(mer));
var reglages = NavMesh.GetSettingsByID(0); // agentRadius 0.6, agentHeight 2, agentSlope 40, agentClimb 0.6, voxelSize 0.5, tileSize 64, overrideVoxelSize/TileSize = true, minRegionArea 4
navMeshData = new NavMeshData();
var op = NavMeshBuilder.UpdateNavMeshDataAsync(navMeshData, reglages, sources, boundsIle);   // bounds = AABB de l'île (pas toute la carte) ; 150-800 ms sur threads de travail
yield return op; NavMesh.AddNavMeshData(navMeshData);
```

- Les meshes de navigation par aire réutilisent les tableaux de sommets du terrain, filtrés par aire (≤ 4 sources par chunk) ; ils ne sont pas rendus.
- **Coût du bake** : non estimable sur le papier (≈ 5,8 M colonnes de voxels à 0,5 m sur l'AABB de l'île, coût ≈ quadratique en résolution). Il est **mesuré dès le jalon J2** (spike) sur l'appareil cible avec un mesh factice de 35 k triangles sur 1,4 km², voxel 0,5 et 0,66, tileSize 64 et 128. Budget explicite **≤ 1 s** ; parade structurelle si dépassement : bake **par tuiles** (`UpdateNavMeshDataAsync` avec bounds successifs) pendant l'écran de chargement jouable en 2D, puis agentRadius 0,5 / voxel 0,5 (2 voxels par diamètre, acceptable pour des passages ≥ 28 m). Le voxel 0,66 n'est qu'un dernier recours (érosion des passages ± 0,7 m).
- **Mise à jour partielle** : `UpdateNavMeshDataAsync(navMeshData, reglages, sources, boundsLocal)` avec `boundsLocal` = AABB modifié + 32 m : seules les tuiles touchées sont reconstruites (10-30 ms). Réservée aux modifications rares du terrain.
- **Bâtiments** : `NavMeshObstacle` avec carving ; aucun rebake.
- **Ponts** : `NavMeshLink` posé au centre de l'emplacement, `startPoint/endPoint` sur les berges à **± (w/2 + 0,5 + agentRadius + 2 × voxelSize) ≈ w/2 + 2,2 m** du centre (au-delà de l'érosion du navmesh par le rayon d'agent), à la hauteur de la berge ; `width = 6`, `bidirectional = true`, `area = Walkable`, `activated = false` jusqu'à la fin de la construction ; après activation, `NavMesh.SamplePosition` (maxDistance 1 m) vérifie chaque extrémité (test J10). Option pour ponts larges traversables en formation : tablier en source Walkable + rebake local par `bounds` (10-30 ms).
- **Coûts d'aires** : marais × 1,6, forêt × 1,2 via `NavMesh.SetAreaCost` au chargement.
- **Vérification R15** (éditeur et tests automatisés), **locale puis globale** — le graphe des groupes étant connexe, un simple `CalculatePath` entre zones voisines réussirait toujours et ne détecterait rien :
  - (a) pour chaque arête fine inter-groupes **hors passage** : `NavMesh.Raycast(pA, pB)` où `pA`/`pB` sont les points à 3 m de part et d'autre du milieu de l'arête, projetés par `NavMesh.SamplePosition` (maxDistance 2 m) → doit renvoyer **true** (bloqué) ; si l'un des deux points n'a pas de navmesh à 2 m, l'arête est comptée bloquée ;
  - (b) pour chaque **passage** : `Raycast` entre les cellules de part et d'autre du passage → doit renvoyer **false** (ouvert) ;
  - (c) connexité globale : `CalculatePath` (positions projetées par `SamplePosition`) entre les 6 départs et sur chaque arête de `Gg` portant un passage, en vérifiant que chaque corner du `NavMeshPath` projeté sur sa cellule (grille spatiale) reste dans l'**union des deux groupes**, qu'aucun segment de la polyligne ne coupe une arête fine inter-groupes hors passage (intersection segment/segment contre les chaînes), et que la longueur ≤ 1,5 × distance euclidienne ;
  - (d) pour chaque pont : les deux extrémités du `NavMeshLink` tombent sur le navmesh (`SamplePosition` 1 m).

---

## 8. Outil éditeur

`EditorWindow` UI Toolkit « Map Heroic / Générateur de carte » :

- **Barre** : champ seed (ulong) + « Aléatoire », sélecteur `ParametresGeneration`, nombre de joueurs (2-6), case « Auto-régénérer », boutons **Générer (2D)**, **Construire 3D**, **Cuire navmesh**, **Vérifier la navigation**, **Tester N seeds**, **Exporter MapData**, **Enregistrer MapAsset**.
- **Preview 2D** (`VisualElement` + `Painter2D`, zoom/pan) avec calques activables : cellules fines (gris fin), remplissage par terrain (couleurs des `DefinitionTerrain`), hachures massif, pieds de massif (points ocre), lacs et rivières (bleu, largeur proportionnelle), gués (tirets bleu clair), contours de zones (1 px), contours de groupes (3 px noir), segments inter-groupes colorés par type (rouge massif, bleu rivière, cyan mer, violet lac), passages (pastilles vertes : col/gué/bande côtière) et cellules réservées (hachures vertes), emplacements de ponts (orange), départs (étoiles numérotées), sockets (mines ▲, pêche ●), champ de crêtes, flux et bassins (calques de diagnostic). Survol : infobulle (zone, groupe, terrain, aire, riveraine, sockets, voisines, part constructible).
- **Panneau de validation** (✓/✗ + valeur mesurée vs seuil, dans l'ordre R1..R18) : ratio terre et marge de bord ; nb zones ; aire min/méd/max et écart max ; compacité moyenne ; histogramme des tailles de groupes ; degrés min/max ; connexité `Gg` ; couverture des frontières (%) ; dureté moyenne avant matérialisation ; nombre de massifs ; % de frontières sans relief sous-jacent ; largeur réelle min des cols ; quotas vs cibles ; zones riveraines ; sockets par massif ; constructible min ; **composantes praticables par groupe** ; pente max des faces massif ; distances entre départs (min mesurée vs `distMin`, sauts min, écart-type) ; écart de score des départs ; nombre de rejeux par phase et total ; dérivation de seed (k) ; temps par phase (ms) ; hash discret et hash hauteurs.
- **Tester N seeds** (défaut 200) : campagne sans preview, CSV dans `Assets/Generation/Rapports/` (seed, k de dérivation, phase, règle violée, nb rejeux par phase, ms par phase, hash) ; affichage du taux de succès sans rejeu / avec rejeu / avec dérivation / échec ; **alerte si taux de rejeu > 5 % ou taux de dérivation > 0** ; export de la liste des seeds validés pour le lobby.
- **Construire 3D** instancie `CarteRacine` (chunks, massifs, eau, texture `DistanceRive`, rivières, décor) via la même classe `ConstructeurCarte3D` qu'au runtime ; **Vérifier la navigation** exécute les tests R15 (a)-(d) et liste en rouge les arêtes, passages et ponts non conformes (massif percé, col bouché, lac fuyant, lien de pont hors navmesh).

---

## 9. Performance mobile et déterminisme

**Budget CPU** (Snapdragon 7 Gen 1 / A14, IL2CPP, sans Burst ; ≈ 7 000 cellules, ≈ 14 000 coins) :

| Phase | Estimation |
|---|---|
| P1 Poisson-disc (≈ 6 200 points) | 8 ms *(mesuré 30 ms en éditeur Mono)* |
| P2 Delaunay × 3 + Voronoï + CSR | 40 ms *(mesuré 25 ms en éditeur Mono)* |
| P3 île + BFS + articulations | 6 ms |
| P4 relief, priority-flood, flux (≈ 14 000 coins) | 15 ms |
| P5 zones + équilibrage + articulations | 40 ms |
| P6 groupes (glouton + 2 000 itérations + dissolution locale) | 8 ms |
| P7 passages | 3 ms |
| P8 matérialisation + Dijkstra rivières + BFS connexité + pente + sockets | 25 ms |
| P9-P11 départs, terrains, validation, hash | 8 ms |
| **Génération pure** | **≈ 150 ms** (thread de fond) |
| Meshes 16-20 chunks + massifs + eau + rubans + texture `DistanceRive` | 60-90 ms (thread principal, 2-3 frames) |
| Décor combiné (CombineMeshes par chunk et prefab) | 40 ms |
| Navmesh (AABB de l'île, voxel 0,5) | 150-800 ms sur threads de travail (**à mesurer au J2**, budget ≤ 1 s) |
| **Total** | **≈ 0,5-1,1 s**, cible < 1 s, limite 2 s |

Leviers : SoA et tampons réutilisés (< 3 Mo alloués par génération, mesuré par `ProfilerRecorder`), pas de LINQ dans le pipeline final, `NativeArray` pour les buffers de mesh. Plan B navmesh si P95 > 1 s sur appareil : bake par tuiles étalé pendant l'écran de chargement jouable en 2D, puis agentRadius 0,5 ; voxel 0,66 en tout dernier recours. Rendu : 16-20 chunks + sous-meshes massifs + eau + bordures ≈ 45 draw calls + décor combiné (1 par prefab et par chunk visible) ; ombres directionnelles 1 cascade 30 m ou désactivées (AO simulée par vertex color) ; Depth Texture, Opaque Texture, HDR et MSAA désactivés sur l'asset URP Mobile (le shader d'eau maison n'en a pas besoin).

**Déterminisme** :
- PRNG xoshiro128\*\* + SplitMix64, sous-flux par phase et par rejeu ; jamais `UnityEngine.Random`, `System.Random`, `Mathf.PerlinNoise`.
- Aucune itération sur `Dictionary`/`HashSet` ; tris `Array.Sort` avec clé composite `(valeur, id)` ; parcours par id croissant.
- Prédicats géométriques (`orient2d`, `incircle`) en **entiers longs** sur coordonnées × 1024 ; flottants ailleurs limités à `+ − × ÷ sqrt` ; `sin/atan2/exp` par tables ; conversions float → int clampées ; coordonnées quantifiées à 1/1024 m ; hauteurs quantifiées à 0,05 m (`short × 20`) dans `MapData`.
- **Contraction FMA** : sur ARM64, clang contracte par défaut `a*b+c` en FMA alors que l'éditeur Windows x64 ne le fait pas. Le flag `-ffp-contract=off` est imposé sur les **deux** plateformes : Android via `PlayerSettings.SetAdditionalIl2CppArgs("-ffp-contract=off")` (API marquée expérimentale : à figer dans un script d'éditeur versionné et à vérifier à chaque mise à jour d'Unity), iOS via un `IPostprocessBuildWithReport` qui ajoute `OTHER_CPLUSPLUSFLAGS = -ffp-contract=off` au `PBXProject` (`UnityEditor.iOS.Xcode`). Un **test à l'exécution** (expression `a*b+c` avec valeurs choisies pour différer sous FMA) refuse le multijoueur si la plateforme contracte.
- **Spike « hash inter-plateformes » au jalon J1** : `Rng` + `Bruit` + un mini-pipeline flottant (fbm, longueurs, tri par clés) hashés sur éditeur, Android arm64 IL2CPP et iOS. Si le spike échoue, les décisions de P3 à P11 basculent en **virgule fixe 32.32 (`long`)** avant J3 ; la conception ne dépend pas de ce choix.
- Hash FNV-1a 64 sur la **partie discrète seule** (section 4, P11) ; un hash « hauteurs » séparé, informatif, non bloquant ; **Burst interdit** dans P0-P11 (autorisé plus tard sur les meshes, cosmétique) ; `Task` de fond sans parallélisme interne.
- Budget global de rejeux (≤ 12) et compteur d'opérations simulé (≤ ≈ 400 ms) avec dérivation de seed bornée (k ≤ 4, section 3) : tous les clients suivent la même cascade ; la campagne de seeds vise un taux de dérivation nul sur 10 000 seeds et le lobby ne propose que des seeds validés.
- Tests : 30 seeds de référence avec hash attendu, exécutés via Test Runner en Edit Mode **et** sur appareil Android/iOS ; lobby : l'hôte publie `(Seed, Derivation, Version, Hash)`, un client au hash divergent est refusé avec message clair, puis reçoit le `MapData` binaire compressé (≈ 200 Ko) et reconstruit la 3D à partir de lui (la 3D étant dérivée de `MapData`, la partie reste jouable).
- Le pathfinding NavMesh n'est pas bit-identique entre plateformes : sans conséquence tant que le multijoueur n'est pas en lockstep (question ouverte Q3).

---

## 10. Assets graphiques recommandés

Ligne directrice : **deux familles seulement, une famille par catégorie** — végétation et rochers = KayKit Forest exclusivement ; bâtiments de possession, mine, moulin = KayKit Medieval Hexagon ; Quaternius Ultimate Fantasy RTS uniquement pour ce que KayKit n'a pas (cultures/Farm Dirt pour la plaine fertile et l'argile, stades de Town Center, port si confirmé à l'inventaire) ; Quaternius Ultimate Nature uniquement pour cactus (désert), palmiers (côte) et arbres morts (marais). Une **palette de projet unique (24 couleurs)** appliquée en couleurs de vertex par un script d'import ; un seul shader flat (ddx/ddy) pour le terrain et les props (les props ayant leurs sommets dupliqués par face).

**Script d'import « palette projet »** (`AssetPostprocessor.OnPostprocessModel`, FBX uniquement — Unity n'a pas d'importeur glTF natif) :
1. fusionner les sous-meshes ;
2. pour chaque triangle, couleur source = échantillon de la texture au centroïde UV (atlas dégradé KayKit) ou `_BaseColor` du matériau du sous-mesh (Quaternius, non texturé) ;
3. couleur cible = plus proche entrée de la palette en distance **Oklab**, avec une **table de correspondance éditable par pack** (par exemple « feuillage KayKit → vert forêt projet », « feuillage → vert-brun terne » pour la variante marais produite par le script et non par un pack) ;
4. écrire `Color32` par sommet en **dupliquant les sommets par face** (props petits, coût nul) ; les faces dont la couleur d'atlas appartient aux 4 teintes d'équipe KayKit sont marquées **masque joueur** : RGB = blanc, A = 1 ; le shader props calcule `couleur = lerp(vertex.rgb, _CouleurJoueur, vertex.a)` avec `_CouleurJoueur` par `MaterialPropertyBlock` / propriété instanciée → nombre de joueurs illimité, un seul matériau ;
5. supprimer les références de textures, assigner l'unique matériau flat ;
6. **normaliser l'échelle par catégorie** sur l'AABB (arbre 6-9 m, maison 5-6 m, rocher 1-3 m, mine ≈ 6 m) — les FBX Quaternius sortent souvent à × 100.
Le remappage n'est pas « sans perte » (l'atlas KayKit est un dégradé, échantillonné par face) : il est validé sur 3 modèles de chaque pack (arbre, bâtiment, rocher) **avant** J12, comme critère du jalon J9.

| Pack | Auteur | Licence | Attribution requise | Couverture | Lien | Prix |
|---|---|---|---|---|---|---|
| KayKit — Forest Nature Pack | Kay Lousberg | CC0 1.0 | non | **Toute la végétation et tous les rochers** du projet : arbres, pins, buissons, herbe, souches, rochers. Gratuit : 100+ modèles, **1 colorisation** ; Extra 9,99 $ : 200+ modèles, 8 variantes de couleur, pièces de terrain modulaires (variantes inutiles avec le remappage de palette) | https://kaylousberg.itch.io/kaykit-forest | Gratuit (Extra 9,99 $) |
| KayKit — Medieval Hexagon Pack | Kay Lousberg | CC0 1.0 | non | Bâtiments RTS complets en 4 couleurs de joueur (maison, forge, scierie, moulin à eau, **mine**, marché, tour, caserne) ; ignorer les tuiles hex et les montagnes à empreinte hexagonale | https://kaylousberg.itch.io/kaykit-medieval-hexagon | Gratuit (Extra 9,99 $ : unités, textures biomes) |
| KayKit — Resource Bits | Kay Lousberg | CC0 1.0 | non | Props de ressources (bois, pierre, fer, argile par recolorisation) pour dépôts, marqueurs de sockets et piquets de pont | https://kaylousberg.itch.io/resource-bits | Gratuit (Extra 4,99 $) |
| KayKit — Medieval Builder Pack | Kay Lousberg | CC0 1.0 | non | À inventorier : pont réutilisable éventuel, éléments de chantier | https://kaylousberg.itch.io/kaykit-medieval-builder-pack | Gratuit |
| Ultimate Fantasy RTS | Quaternius | CC0 1.0 | non | 128 modèles : bâtiments à stades d'évolution (Town Center, maisons, caserne, château) et éléments nature ; non texturé → recoloration triviale. Cultures/Farm Dirt, port et « Mountain » **à confirmer par inventaire du zip au J12** (non listés sur la page officielle) | https://quaternius.com/packs/ultimatefantasyrts.html | Gratuit |
| Ultimate Nature Pack | Quaternius | CC0 1.0 | non | 150+ modèles nature flat-shaded ; utilisés **uniquement** pour cactus, palmiers, arbres morts | https://quaternius.com/packs/ultimatenature.html | Gratuit |
| Stylized Water Shader (miroir bmjoy, original tojynick) | tojynick | MIT (notice obligatoire) | oui | **Référence de lecture seulement** : exige Depth + Opaque Texture, testé Unity 2021 / URP 12, dépôt d'origine supprimé — incompatible avec l'asset Mobile. Crédit « tojynick 2022 » dans `CREDITS.md` si un fragment est repris | https://github.com/bmjoy/Stylized-Water-Shader-Unity-URP | Gratuit |
| URP Stylized Water Shader — Proto Series | BitGem | Asset Store EULA | non | Alternative **à vérifier après import sur URP 17** (critère : compile et fonctionne sans depth texture) ; non maintenu (v1.0, 2021) | https://assetstore.unity.com/packages/vfx/shaders/urp-stylized-water-shader-proto-series-187485 | Gratuit |
| Article « Flat-shaded models via ddx/ddy » | Hextant Studios | Article de référence, aucun fichier réutilisé (licence non publiée, graphe fourni en HDRP) | non | Technique des normales plates par dérivées ; le graphe URP (5 nœuds) est reconstruit, pas copié | https://hextantstudios.com/unity-flat-low-poly-shader/ | Gratuit |
| DelaunatorSharp (core seulement) | nol1fe | MIT | oui | Delaunay 2D (base de P2, vendoré dans `Assets/Plugins`, prédicats remplacés par des versions entières) | https://github.com/nol1fe/delaunator-sharp | Gratuit |
| **Chemin d'achat A (recommandé)** — KayKit Extra ×3 | Kay Lousberg | CC0 1.0 | non | Unités, textures de biomes/saisons, 350+ bâtiments, même atlas | itch.io (liens ci-dessus) | ≈ 25 $ au total |
| **Chemin d'achat B (bascule de style, à faire d'un bloc)** — POLYGON Starter (gratuit) → Vikings → Nature → Fantasy Kingdom | Synty Studios | Asset Store EULA / One Time Purchase | non | Starter : palette ; Vikings : montagnes, bateaux, pêche, bâtiments nordiques ; Nature : forêt, marais, rochers, eau, pont ; Fantasy Kingdom : 2 100 prefabs bâtiments | https://assetstore.unity.com/packages/3d/environments/polygon-starter-pack-art-by-synty-156819 ; https://assetstore.unity.com/packages/3d/environments/polygon-vikings-pack-art-by-synty-85664 ; https://assetstore.unity.com/packages/3d/environments/polygon-nature-pack-art-by-synty-120152 ; https://assetstore.unity.com/packages/3d/environments/polygon-fantasy-kingdom-pack-art-by-synty-164532 | 0 $ / 29,99 $ / 49,99 $ / 349,99 $ |
| Option shader payant — Minimalist Lowpoly Flat/Gradient | Scrollbie | Asset Store EULA | non | Dégradés par face, brouillard de hauteur ; le Shader Graph maison couvre 80 % du besoin | https://assetstore.unity.com/packages/vfx/shaders/minimalist-lowpoly-flat-gradient-shader-91366 | 35,90 $ |

Retirés de la liste : VoronatorSharp (float, second triangulateur), Clipper2 (offset en doubles avec libm, remplacé par un décalage maison le long des normales d'arête pour les rubans), les trois kits Kenney (troisième famille de silhouettes et d'échelle ; leurs manques sont couverts par Quaternius Ultimate Nature), Fantasy Skybox FREE (130 Mo pour un ciel peu visible : **skybox procédural** Shader Graph dégradé 2 couleurs de la palette, 0 Mo, ou cubemap 256² maison).

**Terrain → assets utilisés** (teintes séparées d'au moins 25° de teinte ou 30 % de luminance pour rester lisibles sur téléphone à zoom 160 m ; test de distinction sur appareil dans la recette J8/J9 sur 20 seeds)

| Terrain / élément | Sol (vertex color) | Props et modèles |
|---|---|---|
| Plaine fertile | vert clair | KayKit Forest : herbe, buissons ; Quaternius Fantasy RTS : cultures (si confirmées à l'inventaire) |
| Plaine argileuse | **terracotta #B5573A** | Motif généré : 2-3 ellipses plates « flaques d'argile » plus sombres par cellule ; Quaternius Fantasy RTS « Farm Dirt » (si confirmé) comme fosses d'extraction ; KayKit Resource Bits (argile recolorée) |
| Forêt | vert sombre | KayKit Forest : arbres et pins (2-3 par cellule, variantes ≤ 150 triangles) |
| Colline | vert-jaune, relief accentué | KayKit Forest : rochers |
| Montagne (zone) | vert-gris ; massif en gris roche, neige ≥ hBase + 14 m | Crête continue générée + rochers KayKit Forest sur les pieds ; prop de crête Quaternius seulement si confirmé à l'inventaire ; pins épars |
| Désert | **jaune pâle #E8D9A0** | Quaternius Ultimate Nature : cactus ; rochers clairs KayKit Forest |
| Marais | vert-brun terne | Quaternius Ultimate Nature : arbres morts ; KayKit Forest recoloré par la table « marais » du script d'import ; roseaux ; flaques (quads d'eau) |
| Côte/plage | **sable blanchi #F2EBD3**, sable mouillé via `DistanceRive` | Quaternius Ultimate Nature : palmiers ; rochers de bord de mer KayKit Forest ; port/ponton : Quaternius si confirmé, sinon ponton généré comme le tablier |
| Mer | plan d'eau | Shader d'eau maison : deux teintes, écume par `DistanceRive` |
| Lac | polygone d'eau | Même shader, teinte plus sombre, roseaux KayKit sur la rive |
| Rivière | ruban d'eau creusé | Même shader, défilement UV ; galets (KayKit rochers) sur les gués |
| Pont | — | **Tablier généré** (section 7.3) ; KayKit Medieval Builder si un pont y est confirmé |
| Mine | marqueur socket ocre | KayKit Hexagon mine ; Resource Bits minerai |
| Bâtiment de prise de possession | — | KayKit Hexagon (masque joueur par canal alpha, un seul matériau) ou Quaternius Town Center (stades d'évolution) |
| Bordures de pays | ruban unlit couleur joueur | Généré |

---

## 11. Plan d'implémentation par jalons

| Jalon | Durée | Livrables (dossiers `Assets/MapHeroic/…`) | Critères de validation mesurables |
|---|---|---|---|
| **J1 — Socle et spike déterminisme** | 3 j | `Generation/Noyau/` : `Rng.cs`, `Bruit.cs`, `Tables.cs`, `HashFnv.cs`, structures CSR ; tests EditMode ; **spike « hash inter-plateformes »** (Rng + Bruit + mini-pipeline flottant) sur éditeur, Android arm64 IL2CPP et iOS avec `-ffp-contract=off` sur les deux plateformes ; test d'exécution FMA ; `com.unity.mathematics` dans le manifest | Même seed → mêmes 10 000 tirages ; 0 allocation dans `Rng` ; hash identique sur les 3 plateformes (sinon décision virgule fixe 32.32 avant J3) ; 0 NaN dans Tables/Bruit |
| **J2 — Géométrie et spike navmesh** | 4 j | `Generation/Geometrie/` : `PoissonDisc.cs`, DelaunatorSharp vendoré avec prédicats entiers, `Voronoi.cs`, `Lloyd.cs`, `GrapheCellules.cs` (+ `ReconstruireDerives`) ; **spike de bake navmesh** sur appareil (mesh factice 35 k triangles, 1,4 km², voxel 0,5/0,66, tuile 64/128) | 6 500-7 500 cellules ; Delaunay 7 000 points < 8 ms en éditeur ; tableaux identiques entre 2 exécutions ; temps de bake mesuré et plan (complet ou par tuiles) arrêté pour tenir ≤ 1 s |
| **J3 — Île, relief, hydrologie** | 3 j | `Ile.cs` (marge, articulations), `Relief.cs` (crêtes ridged), `Hydrologie.cs` (priority-flood, flux, dureté, bassins) ; preview 2D minimale | 100 seeds : ratio terre ∈ [0,26 ; 0,38] avec ≤ 3 rejeux ; 0 cellule terre à < 48 m du bord ; 100 % des coins ont un chemin descendant ; 4-6 rivières naturelles ; crêtes sur 20-30 % des cellules ; pente moyenne < 4° |
| **J4 — 90 zones** | 3 j | `Zones.cs` (germes relaxés, croissance à clé additive, équilibrage par somme des carrés, compacité relative, articulations) | 200 seeds : 100 % à 90 zones connexes, 100 % dans ± 30 %, ≥ 95 % dans ± 25 % ; 0 articulation isolant < 3 zones. **Atteint sur 25 graines : écart max 24,2 %, 100 % dans ± 25 %, 113 ms** |
| **J5 — Groupes et passages** | 4 j | `Groupes.cs` (décomposition, glouton, énergie, dissolution locale), `Passages.cs` (éligibilité ≥ 45 m, Kruskal borné, degrés cibles, localisation 2-4 arêtes, cellules réservées) | 200 seeds : 100 % tailles ∈ {3, 4}, degrés ∈ [1, 3], `Gg` connexe ; ≤ 5 % de seeds avec rejeu ; dureté moyenne avant matérialisation ≥ 0,5 (seuil revu après mesure) ; 100 % des passages ≥ 28 m |
| **J6 — Matérialisation** | 5 j | `Frontieres.cs` (segments, typage par rang, rivières avec plancher, massifs asymétriques, cols, gués, lacs, plafond 45 %, connexité, pente, sockets) | 100 % des arêtes inter-groupes hors passage bloquantes ; 100 % des rivières atteignent mer ou lac, 0 coin de rivière < 1,5 m hors embouchure ; 6-10 massifs ; constructible ≥ 55 % par zone ; 1 composante praticable par groupe ; largeur réelle des cols ≥ 24 m ; ≥ 1 socket par massif |
| **J7 — Départs, terrains, ponts, validation** | 3 j | `Departs.cs`, `Terrains.cs`, `Ponts.cs`, `Validation.cs`, `MapData.cs` + sérialisation binaire compressée et JSON | Quotas ± 2 (désert/marais ≤ 6) ; 6 départs à ≥ 3 sauts (repli 2) et ≥ `distMin`, degré ∈ [2, 3], score ± 20 % ; hash discret identique sur 30 seeds de référence ; taille du `MapData` compressé ≤ 250 Ko |
| **J8 — Éditeur complet** | 3 j | `Editor/FenetreGenerateur.cs`, `PreviewCarte.cs`, `PanneauValidation.cs`, `CampagneSeeds.cs` | Toutes les stats R1..R18 affichées ; 1 000 seeds en lot sans échec définitif, taux de dérivation 0 ; CSV produit ; génération 2D < 200 ms en éditeur ; recette visuelle 20 seeds (décision `SubdivisionsArete`, lisibilité argile/désert/côte) |
| **J9 — Mesh 3D, eau, massifs, décor** | 6 j | `Construction3D/ConstructeurCarte3D.cs`, `MeshChunk.cs`, `MeshEau.cs`, `TextureDistanceRive.cs`, `MeshRivieres.cs`, `Bordures.cs`, `Decor.cs`, Shader Graph terrain (Lit, normale monde) + eau maison + skybox procédural, script d'import palette, caméra | ≤ 40 k triangles terrain ; ≤ 50 draw calls hors décor ; 0 triangle > 40° hors massifs, 0 face massif > 60° ; ≤ 250 k triangles de décor et ≤ 1 200 instances visibles à zoom 160 m ; build mesh < 100 ms ; 60 fps sur appareil cible (vue zoomée et dézoomée) ; script d'import validé sur 3 modèles par pack |
| **J10 — Navmesh et ponts** | 4 j | `Navigation/ConstructeurNavMesh.cs` (sources ModifierBox/mesh, bake async ou par tuiles, mise à jour partielle), `Pont.cs` (`NavMeshLink`), `VerificationNavigation.cs` (tests (a)-(d)) | Bake ≤ 1 s sur appareil ; tests R15 (a)-(d) 100 % conformes sur 50 seeds ; activation d'un pont crée un chemin sans rebake, extrémités du lien sur le navmesh ; rebake local < 80 ms |
| **J11 — Mobile et déterminisme** | 3 j | Builds Android/iOS, profil, tests sur appareil, lobby de comparaison de hash (stub), envoi de secours du `MapData` compressé | Génération + 3D + navmesh < 2 s (cible < 1 s) P95 sur 50 seeds ; hash identique Android/iOS/éditeur sur 30 seeds ; < 3 Mo alloués par génération ; mémoire carte < 60 Mo |
| **J12 — Direction artistique** | continu | Inventaire des zips (Quaternius Fantasy RTS, KayKit Builder), intégration KayKit + Quaternius via le script d'import, tests de lisibilité des groupes et passages | Palette unique ; 1 matériau terrain + 1 matériau props + 1 eau ; une famille par catégorie respectée ; retours utilisateurs sur 5 testeurs |

Total indicatif : ≈ 41 jours de développement pour l'étape 1, la 3D n'étant entreprise qu'après validation en lot de la structure (J8).

---

## 12. Risques et parades

1. **Aires ou groupes impossibles sur certaines îles** (goulets étroits, langues côtières) → articulations traitées dès P3 (petites langues converties en mer, isthmes rejetés), contrôle de compacité en P5, rejeu par sous-flux, retour à P3 en dernier recours, dérivation de seed bornée ; la campagne de seeds mesure le taux de rejeu (alerte > 5 %) et de dérivation (objectif 0).
2. **Arbre couvrant à degré ≤ 3 introuvable** (groupe à 5-6 voisins, ou sous-graphe éligible ≥ 45 m non connexe) → 50 remélanges puis **retour à P6** ; jamais de degré 4.
3. **Rivière prolongée qui bouche un passage ou traverse un groupe** → coût infini sur toutes les arêtes incidentes aux coins de passage et aux cellules réservées, gués tous les 3-4 coins sur les segments intra-groupe, terminaison en lac au-delà de 40 coins ou au plancher ; contrôle final « aucune boîte de rivière n'intersecte un rectangle de passage » ; test unitaire dédié.
4. **Fuites de navigation** (massif effilé franchissable, interstice entre boîtes de rivière, bord de lac) → sous-mesh massif NotWalkable explicite, ModifierBox de rivière avec recouvrement de 1 m, mesh de lac exact (pas d'AABB), plafond de pente 40°, tests R15 (a)-(d) locaux et globaux dans l'éditeur.
5. **Aspect « obstacle = frontière »** → relief et hydrologie préexistants orientant zones et groupes, chaînes continues avec prolongements effilés aux extrémités mer/jonction, rivières intra-groupe à gués, lacs hors frontières, bosquets isolés, biomes contigus ; revue visuelle de 20 seeds à chaque jalon ; métrique « % de frontières sans relief sous-jacent ».
6. **Massif qui mange trop d'espace constructible ou déconnecte une zone** → plafond 45 % (Massif + Lac) sans changement de zone, amincissement, retrait des prolongements, retypage en rivière, BFS de connexité des cellules praticables par zone et par groupe en P8 (mesure R10 au panneau).
7. **Non-déterminisme flottant inter-plateformes** (FMA, libm, conversions hors plage) → spike J1, `-ffp-contract=off` Android et iOS, test FMA à l'exécution, prédicats entiers, tables, conversions clampées, quantification, hash discret seul, hash de référence en CI, refus en lobby puis envoi du `MapData` compressé ; bascule en virgule fixe 32.32 si le spike échoue.
8. **Temps de bake navmesh sur bas de gamme** → mesure au J2, bounds limités à l'AABB de l'île, bake asynchrone pendant l'écran de chargement, bake par tuiles jouable en 2D, agentRadius 0,5, voxel 0,66 en dernier recours, géométrie de bake réduite (terrain sans décor).
9. **Sur-allocation / GC** → SoA, tampons réutilisés, `ProfilerRecorder` en test automatisé (< 3 Mo).
10. **Cohérence visuelle des packs gratuits** → deux familles seulement, une famille par catégorie, palette unique et échelle normalisée par le script d'import (validé sur 3 modèles par pack avant J12), un shader ; inventaire des zips avant toute dépendance (Quaternius Fantasy RTS : cultures, port, montagne non confirmés) ; bascule Synty possible sans changer le pipeline.
11. **Lisibilité de 90 zones sur écran mobile** → bordures épaisses projetées sur le relief, couleurs de terrain saturées et séparées (argile terracotta, désert jaune pâle, côte sable blanchi), contour de groupe optionnel, frontières physiquement visibles (vallées en V, crêtes continues), infobulle de zone, zoom minimal 160 m.
12. **Départs perçus comme injustes** → filtre structurel (constructible, lac, massif, composante unique, degré 2-3), score structurel (aire, sockets, exposition) et écart-type des distances affichés ; normalisation des terrains du groupe ; possibilité d'ajouter des nœuds neutres (tribus) plus tard sans changer la structure.
13. **Budget de rendu du décor** → densité forêt 2-3 arbres par cellule, variantes ≤ 150 triangles, combinaison par chunk et prefab, bosquets combinés à zoom élevé, compte de triangles visibles au J9 (≤ 250 k).
14. **Dérive de périmètre pour un solo** → chaque jalon livre un incrément testable dans l'éditeur ; la 3D commence après J8 ; les options (bruitage d'arêtes, rebake local de pont, Burst, prop de crête) restent désactivables.
15. **Bibliothèques tierces et licences** → DelaunatorSharp seul vendoré dans `Assets/Plugins` (version figée, MIT, notice dans `CREDITS.md`) ; aucune bibliothèque dans P0-P11 hormis lui ; le shader tojynick (MIT) n'est qu'une référence de lecture, crédité s'il est cité ; Hextant : article de référence, aucun fichier réutilisé ; assets CC0 sans attribution requise mais listés par courtoisie.

---

## 13. Questions ouvertes pour le client

1. **Rivières intra-groupe** : les segments de rivières naturelles qui traversent l'intérieur d'un groupe conservent leur largeur et donnent du poisson, mais sont franchissables par des gués tous les 3-4 coins (sans construction). Acceptez-vous ce compromis, ou préférez-vous que ces segments soient supprimés pour que « toute eau bloque » (au prix de réseaux hydrographiques moins crédibles) ?
2. **Échelle** : 1400 × 1400 m avec une île d'≈ 610 000 m² (zone ≈ 6 750 m², traversée ≈ 20 s à 4 m/s) vous convient-il, ou souhaitez-vous des zones plus vastes (paramètre `TailleCarte`, sans autre changement) ?
3. **Multijoueur** : le modèle réseau sera-t-il en lockstep déterministe (auquel cas le pathfinding devra aussi être déterministe, ce qui exclut le NavMesh Unity) ou en autorité serveur/hôte (compatible avec cette conception) ?
4. **Direction artistique** : partez-vous sur la famille KayKit + Quaternius (gratuite, achat ≈ 25 $ ensuite) ou souhaitez-vous d'emblée le style Synty POLYGON (Starter gratuit, puis 30-80 $ pour Vikings + Nature) ? Les deux sont compatibles avec le pipeline, mais il faut choisir une seule famille.
5. **Pêche** : la ressource poisson nécessite-t-elle un bâtiment sur un socket de pêche (comme les mines), ou est-elle acquise automatiquement par la possession d'une zone riveraine ?
6. **Mines et zone Montagne** (décision proposée, à valider) : chaque massif désigne une zone Montagne unique et ses sockets de mines n'existent que dans cette zone ; les autres zones que le massif borde l'englobent comme relief non constructible sans minerai. Cela respecte « une zone = un seul terrain » et « mines au pied des montagnes ». L'alternative serait une mine produisant du minerai dans n'importe quelle zone au pied d'un massif (le socket déterminant la ressource d'extraction, le terrain la ressource de surface). Quelle option retenez-vous ?
7. **Côte/plage** : la liste validée ne donne pas de ressource propre à la côte ; nous proposons « aucune ressource, poisson via la riveraineté ». Confirmez-vous, ou souhaitez-vous une nourriture faible sur la côte ?
