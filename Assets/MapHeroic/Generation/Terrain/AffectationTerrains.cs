using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;
using Unity.Mathematics;

namespace MapHeroic.Generation.Terrain
{
    public sealed class ParametresTerrains
    {
        /// <summary>Zones côtières visées (part de plage élevée).</summary>
        public int QuotaCote = 11;

        /// <summary>Maximum de zones sans ressource. Ce sont des maxima, jamais des minima.</summary>
        public int MaximumDesert = 6;
        public int MaximumMarais = 6;

        /// <summary>Parts relatives du reste, une fois montagnes, côtes, déserts et marais posés.</summary>
        public int PartFertile = 18;
        public int PartForet = 18;
        public int PartColline = 12;
        public int PartArgileuse = 10;

        /// <summary>
        /// Prime accordée par zone voisine portant déjà le même terrain. C'est elle qui forme
        /// les biomes. À 0,15 elle ne pesait rien face à des aptitudes comprises entre 0,6 et
        /// 2,2 ; à 0,45, trois voisines de même terrain l'emportent sur un écart d'aptitude
        /// ordinaire, ce qui suffit à agglomérer les régions sans forcer un terrain là où le
        /// sol s'y prête vraiment mal.
        /// </summary>
        public float BonusContiguite = 0.45f;

        /// <summary>Passes d'échange pour affiner les régions après la croissance.</summary>
        public int PassesRegroupement = 4;

        /// <summary>
        /// Nombre de zones visé par région d'un même terrain. Il fixe combien de foyers on
        /// sème : dix-neuf zones de forêt pour une taille visée de six donnent trois massifs
        /// boisés, ce qui se lit, plutôt que dix-neuf taches, ce qui ne se lit pas.
        /// </summary>
        public int TailleRegionVisee = 6;

        /// <summary>
        /// Part de cellules de plage à partir de laquelle une zone est dite côtière.
        /// Calibrée sur la mesure : à 0,5, quatre zones seulement se qualifiaient pour un
        /// quota de onze, car une zone touche rarement la mer sur la moitié de sa surface.
        /// </summary>
        public float PartPlageCote = 0.3f;
        public int EchangesMax = 20;
        public int RejeuxMax = 2;
    }

    public sealed class DiagnosticTerrains
    {
        public int Essais;
        public bool Reussi;
        public string MotifEchec;
        public int[] Compte = new int[8];
        public int NbZonesSansRessource;
        public int NbVoisinagesSansRessource;
        public int NbPonts;
        public int EchangesRegroupement;
        public int NbRegions;

        /// <summary>Part des frontières de zones séparant deux terrains différents.</summary>
        public float PartFrontieresEntreTerrains;
        public long Millisecondes;

        public override string ToString()
        {
            if (!Reussi) return $"terrains ÉCHEC après {Essais} essai(s) : {MotifEchec}";
            var parts = new List<string>();
            for (int i = 0; i < Compte.Length; i++)
            {
                if (Compte[i] > 0) parts.Add($"{Compte[i]} {Terrains.Nom((TypeTerrain)i)}");
            }
            return $"terrains : {string.Join(", ", parts)} ; {NbZonesSansRessource} zones sans ressource " +
                   $"({NbVoisinagesSansRessource} voisinages stériles), {NbPonts} emplacements de pont ; " +
                   $"{NbRegions} régions semées, {EchangesRegroupement} échanges, {PartFrontieresEntreTerrains:P0} " +
                   $"des frontières séparent deux terrains, {Millisecondes} ms";
        }
    }

    public sealed class EmplacementPont
    {
        public int GroupeA;
        public int GroupeB;
        public int[] Aretes;
        public float2 Position;
        public float Largeur;
    }

    /// <summary>
    /// Phase P10 : type de terrain de chaque zone, ressources, et emplacements de ponts.
    ///
    /// Les quotas de désert et de marais sont des MAXIMA, jamais des minima : ce sont les
    /// deux seuls terrains sans ressource, et une carte qui en compterait moins est meilleure,
    /// pas moins conforme. Traiter leurs quotas comme des cibles obligerait à en poser là où
    /// le terrain ne s'y prête pas.
    ///
    /// L'affectation se fait en une seule passe sur des couples (zone, terrain) classés par
    /// aptitude, avec une prime à la contiguïté qui regroupe les mêmes terrains — sans elle,
    /// les types se répartiraient au hasard et la carte n'aurait aucune région lisible.
    /// </summary>
    public static class AffectationTerrains
    {
        public const int Phase = 11;

        public static bool Construire(Carte carte, ParametresTerrains p, Rng rngRacine, out DiagnosticTerrains diag)
        {
            if (carte?.GroupesDepart == null) throw new InvalidOperationException("Les départs doivent précéder les terrains.");

            var chrono = System.Diagnostics.Stopwatch.StartNew();
            diag = new DiagnosticTerrains();

            var interdites = ZonesInterditesAuxTerrainsSteriles(carte);
            var statistiques = Statistiques(carte);

            for (int essai = 0; essai < p.RejeuxMax; essai++)
            {
                diag = new DiagnosticTerrains { Essais = essai + 1 };
                var rng = rngRacine.Deriver(Phase * 100 + essai);

                TypeTerrain[] terrains = Affecter(carte, p, statistiques, interdites, ref rng, diag);
                if (terrains == null) continue;

                carte.TerrainDeZone = terrains;
                carte.RessourceDeZone = new TypeRessource[carte.NbZones];
                for (int z = 0; z < carte.NbZones; z++)
                {
                    carte.RessourceDeZone[z] = Terrains.Ressource(terrains[z]);
                    if (carte.RessourceDeZone[z] == TypeRessource.Aucune && !carte.ZoneRiveraine[z])
                    {
                        diag.NbZonesSansRessource++;
                    }
                    diag.Compte[(int)terrains[z]]++;
                }

                diag.NbVoisinagesSansRessource = CompterVoisinagesSteriles(carte, terrains);
                carte.Ponts = TrouverPonts(carte);
                diag.NbPonts = carte.Ponts.Count;

                diag.Reussi = true;
                diag.Millisecondes = chrono.ElapsedMilliseconds;
                return true;
            }

            diag.Reussi = false;
            diag.Millisecondes = chrono.ElapsedMilliseconds;
            return false;
        }

        // ------------------------------------------------------------- statistiques

        struct StatistiqueZone
        {
            public float Elevation;
            public float Humidite;
            public float PartPlage;
            public float PartMassif;
            public float Aridite;
        }

        static StatistiqueZone[] Statistiques(Carte carte)
        {
            GrapheCellules g = carte.Graphe;
            var stats = new StatistiqueZone[carte.NbZones];

            for (int z = 0; z < carte.NbZones; z++)
            {
                double elevation = 0.0, humidite = 0.0, aridite = 0.0;
                int plage = 0, massif = 0;
                List<int> cellules = carte.CellulesDeZone[z];

                foreach (int c in cellules)
                {
                    double h = 0.0;
                    int n = g.DebutCoins[c + 1] - g.DebutCoins[c];
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++) h += carte.HauteurCoin[g.CoinsDeCellule[s]];
                    elevation += h / n;

                    // L'humidité décroît avec la distance à l'eau, saturée à cinq cellules.
                    humidite += 1.0 - math.min(1f, carte.DistCote[c] / 5f);
                    aridite += Bruit.Fbm(g.Sites[c], 3, 0.004f, 0x5EEDu);

                    if (carte.ADrapeau(c, DrapeauxCellule.Plage)) plage++;
                    if (carte.ADrapeau(c, DrapeauxCellule.Massif)) massif++;
                }

                stats[z] = new StatistiqueZone
                {
                    Elevation = (float)(elevation / cellules.Count),
                    Humidite = (float)(humidite / cellules.Count),
                    PartPlage = (float)plage / cellules.Count,
                    PartMassif = (float)massif / cellules.Count,
                    Aridite = (float)(aridite / cellules.Count)
                };
            }
            return stats;
        }

        /// <summary>
        /// Zones où désert et marais sont proscrits : celles de départ et leurs voisines
        /// directes. Un joueur qui ouvrirait la partie cerné de terres stériles serait
        /// condamné avant d'avoir joué.
        /// </summary>
        static bool[] ZonesInterditesAuxTerrainsSteriles(Carte carte)
        {
            var interdites = new bool[carte.NbZones];
            foreach (int zoneDepart in carte.ZonesDepart)
            {
                if (zoneDepart < 0) continue;
                interdites[zoneDepart] = true;
                foreach (int voisine in carte.ZonesVoisines[zoneDepart]) interdites[voisine] = true;
            }
            return interdites;
        }

        // -------------------------------------------------------------- affectation

        static TypeTerrain[] Affecter(Carte carte, ParametresTerrains p, StatistiqueZone[] stats,
                                      bool[] interdites, ref Rng rng, DiagnosticTerrains diag)
        {
            int n = carte.NbZones;
            var terrains = new TypeTerrain[n];
            var affectee = new bool[n];
            var quota = new int[8];

            // Les montagnes sont imposées par la matérialisation, pas choisies ici.
            int montagnes = 0;
            for (int z = 0; z < n; z++)
            {
                if (!carte.ZoneMontagne[z]) continue;
                terrains[z] = TypeTerrain.Montagne;
                affectee[z] = true;
                montagnes++;
            }

            // Les zones de départ reçoivent d'office un terrain nourricier.
            foreach (int zoneDepart in carte.ZonesDepart)
            {
                if (zoneDepart < 0 || affectee[zoneDepart]) continue;
                terrains[zoneDepart] = TypeTerrain.PlaineFertile;
                affectee[zoneDepart] = true;
                quota[(int)TypeTerrain.PlaineFertile]++;
            }

            int restant = n - montagnes - CompterAffectees(affectee, montagnes);
            int cote = Math.Min(p.QuotaCote, restant);
            int desert = Math.Min(p.MaximumDesert, Math.Max(0, restant - cote));
            int marais = Math.Min(p.MaximumMarais, Math.Max(0, restant - cote - desert));

            int reste = Math.Max(0, restant - cote - desert - marais);
            int total = p.PartFertile + p.PartForet + p.PartColline + p.PartArgileuse;
            var plafonds = new int[8];
            plafonds[(int)TypeTerrain.Montagne] = int.MaxValue;
            plafonds[(int)TypeTerrain.Cote] = cote;
            plafonds[(int)TypeTerrain.Desert] = desert;
            plafonds[(int)TypeTerrain.Marais] = marais;
            plafonds[(int)TypeTerrain.PlaineFertile] = reste * p.PartFertile / total + 4;
            plafonds[(int)TypeTerrain.Foret] = reste * p.PartForet / total + 4;
            plafonds[(int)TypeTerrain.Colline] = reste * p.PartColline / total + 4;
            plafonds[(int)TypeTerrain.PlaineArgileuse] = reste * p.PartArgileuse / total + 4;

            var verrouillee = (bool[])affectee.Clone();   // montagnes et zones de départ
            FaireCroitreLesRegions(carte, p, stats, interdites, terrains, affectee, quota, plafonds, diag);

            // Les zones qu'aucun quota n'a pu prendre reçoivent le terrain le plus disponible.
            for (int z = 0; z < n; z++)
            {
                if (affectee[z]) continue;
                TypeTerrain repli = TypeTerrain.PlaineFertile;
                int marge = int.MinValue;
                foreach (TypeTerrain t in new[] { TypeTerrain.PlaineFertile, TypeTerrain.Foret,
                                                  TypeTerrain.Colline, TypeTerrain.PlaineArgileuse })
                {
                    int disponible = plafonds[(int)t] - quota[(int)t];
                    if (disponible > marge) { marge = disponible; repli = t; }
                }
                terrains[z] = repli;
                affectee[z] = true;
                quota[(int)repli]++;
            }

            RegrouperEnBiomes(carte, p, stats, interdites, verrouillee, terrains, diag);
            return terrains;
        }

        /// <summary>
        /// Regroupe les terrains en régions cohérentes par échanges entre zones.
        ///
        /// La première version ajoutait une prime de contiguïté au moment de choisir le
        /// terrain d'une zone — mais la liste des candidats était triée une fois pour toutes
        /// sur la seule aptitude, si bien que la prime ne changeait jamais l'ordre et ne
        /// servait à rien. Résultat : des terrains éparpillés au hasard, une carte qui
        /// ressemblait à un motif de camouflage plutôt qu'à un paysage.
        ///
        /// Échanger DEUX zones plutôt que réaffecter une seule a une vertu décisive : cela
        /// laisse les quotas rigoureusement inchangés. On peut donc chercher la meilleure
        /// disposition sans jamais avoir à revérifier la répartition des terrains.
        /// </summary>
        static void RegrouperEnBiomes(Carte carte, ParametresTerrains p, StatistiqueZone[] stats,
                                      bool[] interdites, bool[] verrouillee, TypeTerrain[] terrains,
                                      DiagnosticTerrains diag)
        {
            int n = carte.NbZones;

            float Score(int zone, TypeTerrain terrain)
            {
                if (verrouillee[zone]) return float.MinValue;
                if (interdites[zone] && (terrain == TypeTerrain.Desert || terrain == TypeTerrain.Marais))
                {
                    return float.MinValue;
                }

                int voisinesIdentiques = 0;
                foreach (int v in carte.ZonesVoisines[zone])
                {
                    if (terrains[v] == terrain) voisinesIdentiques++;
                }
                return Aptitude(stats[zone], terrain, p) + p.BonusContiguite * voisinesIdentiques;
            }

            for (int passe = 0; passe < p.PassesRegroupement; passe++)
            {
                int echanges = 0;

                for (int a = 0; a < n; a++)
                {
                    if (verrouillee[a]) continue;
                    for (int b = a + 1; b < n; b++)
                    {
                        if (verrouillee[b]) continue;
                        TypeTerrain ta = terrains[a], tb = terrains[b];
                        if (ta == tb) continue;

                        float avant = Score(a, ta) + Score(b, tb);
                        if (float.IsNegativeInfinity(avant)) continue;

                        terrains[a] = tb;
                        terrains[b] = ta;
                        float apres = Score(a, tb) + Score(b, ta);

                        if (apres > avant + 1e-4f) echanges++;
                        else { terrains[a] = ta; terrains[b] = tb; }
                    }
                }

                diag.EchangesRegroupement += echanges;
                if (echanges == 0) break;
            }

            // Mesure du résultat : part des frontières entre zones qui séparent deux terrains
            // différents. Plus elle est basse, plus les régions sont franches.
            int frontieres = 0, differentes = 0;
            for (int z = 0; z < n; z++)
            {
                foreach (int v in carte.ZonesVoisines[z])
                {
                    if (v <= z) continue;
                    frontieres++;
                    if (terrains[z] != terrains[v]) differentes++;
                }
            }
            diag.PartFrontieresEntreTerrains = frontieres > 0 ? (float)differentes / frontieres : 0f;
        }

        /// <summary>
        /// Fait pousser les terrains en régions, depuis quelques germes par type.
        ///
        /// Affecter le terrain zone par zone, puis tenter de recoller par échanges, plafonnait
        /// à une frontière sur deux séparant deux terrains différents — la carte gardait
        /// l'aspect d'un damier. C'est le principe même qui était mauvais : la cohérence d'une
        /// région ne se rattrape pas après coup, elle se construit.
        ///
        /// On sème donc quelques foyers par terrain, espacés, puis on les fait croître de
        /// proche en proche jusqu'à épuiser les quotas, en préférant à chaque pas la zone la
        /// plus apte. C'est le même mécanisme qu'en P5 pour les zones, appliqué un cran plus
        /// haut. Nombre de germes = quota / taille de région visée : dix-neuf zones de forêt
        /// donnent trois massifs boisés d'environ six zones, et non dix-neuf taches.
        /// </summary>
        static void FaireCroitreLesRegions(Carte carte, ParametresTerrains p, StatistiqueZone[] stats,
                                           bool[] interdites, TypeTerrain[] terrains, bool[] affectee,
                                           int[] quota, int[] plafonds, DiagnosticTerrains diag)
        {
            int n = carte.NbZones;

            // La côte n'est pas une région à faire pousser : c'est un liseré, et elle est
            // définie par un fait géographique — la part de plage. On l'attribue donc
            // directement aux zones les plus littorales, avant tout le reste. La faire
            // croître comme les autres la poussait vers l'intérieur des terres et ne lui
            // laissait que deux zones sur les onze prévues.
            AttribuerLeLittoral(carte, p, stats, terrains, affectee, quota, plafonds);

            var file = new FilePrioriteMin(n * 4);
            var entreeZone = new List<int>(n * 4);
            var entreeTerrain = new List<TypeTerrain>(n * 4);
            var entreeDistance = new List<int>(n * 4);

            void Pousser(int zone, TypeTerrain terrain, int distance)
            {
                entreeZone.Add(zone);
                entreeTerrain.Add(terrain);
                entreeDistance.Add(distance);

                // La clé est d'abord la DISTANCE au foyer, l'aptitude ne servant qu'à
                // départager. Trier sur la seule aptitude, comme au premier essai, revenait à
                // laisser chaque terrain rafler ses meilleures zones où qu'elles soient : on
                // retrouvait l'éparpillement qu'on voulait supprimer.
                file.Empiler(distance * 10f - Aptitude(stats[zone], terrain, p), entreeZone.Count - 1);
            }

            foreach (TypeTerrain terrain in new[]
            {
                TypeTerrain.Desert, TypeTerrain.Marais, TypeTerrain.Foret,
                TypeTerrain.PlaineFertile, TypeTerrain.Colline, TypeTerrain.PlaineArgileuse
            })
            {
                int restant = plafonds[(int)terrain] - quota[(int)terrain];
                if (restant <= 0) continue;

                int nbGermes = math.clamp((int)math.round(restant / (float)p.TailleRegionVisee), 1, 4);
                var germes = ChoisirGermes(carte, p, stats, interdites, affectee, terrain, nbGermes);

                foreach (int germe in germes)
                {
                    if (affectee[germe] || quota[(int)terrain] >= plafonds[(int)terrain]) continue;
                    terrains[germe] = terrain;
                    affectee[germe] = true;
                    quota[(int)terrain]++;
                    diag.NbRegions++;
                    foreach (int voisine in carte.ZonesVoisines[germe])
                    {
                        if (!affectee[voisine] && Autorise(p, interdites, voisine, terrain)) Pousser(voisine, terrain, 1);
                    }
                }
            }

            while (file.Depiler(out _, out int entree))
            {
                int zone = entreeZone[entree];
                TypeTerrain terrain = entreeTerrain[entree];
                if (affectee[zone]) continue;
                if (quota[(int)terrain] >= plafonds[(int)terrain]) continue;

                terrains[zone] = terrain;
                affectee[zone] = true;
                quota[(int)terrain]++;

                int distance = entreeDistance[entree] + 1;
                foreach (int voisine in carte.ZonesVoisines[zone])
                {
                    if (!affectee[voisine] && Autorise(p, interdites, voisine, terrain)) Pousser(voisine, terrain, distance);
                }
            }
        }

        /// <summary>
        /// Attribue le terrain Côte aux zones les plus littorales, par part de plage
        /// décroissante. C'est un constat géographique, pas un choix d'aménagement.
        /// </summary>
        static void AttribuerLeLittoral(Carte carte, ParametresTerrains p, StatistiqueZone[] stats,
                                        TypeTerrain[] terrains, bool[] affectee, int[] quota, int[] plafonds)
        {
            var littorales = new List<int>(carte.NbZones);
            for (int z = 0; z < carte.NbZones; z++)
            {
                if (affectee[z] || stats[z].PartPlage < p.PartPlageCote) continue;
                littorales.Add(z);
            }
            littorales.Sort((a, b) =>
            {
                if (stats[a].PartPlage != stats[b].PartPlage) return stats[b].PartPlage < stats[a].PartPlage ? -1 : 1;
                return a < b ? -1 : (a > b ? 1 : 0);
            });

            foreach (int z in littorales)
            {
                if (quota[(int)TypeTerrain.Cote] >= plafonds[(int)TypeTerrain.Cote]) break;
                terrains[z] = TypeTerrain.Cote;
                affectee[z] = true;
                quota[(int)TypeTerrain.Cote]++;
            }
        }

        static bool Autorise(ParametresTerrains p, bool[] interdites, int zone, TypeTerrain terrain)
        {
            if (!interdites[zone]) return true;
            return terrain != TypeTerrain.Desert && terrain != TypeTerrain.Marais;
        }

        /// <summary>
        /// Foyers d'un terrain : les zones qui lui conviennent le mieux, jamais voisines entre
        /// elles — deux germes mitoyens fusionneraient en une seule région et l'on n'aurait
        /// pas la variété voulue.
        /// </summary>
        static List<int> ChoisirGermes(Carte carte, ParametresTerrains p, StatistiqueZone[] stats,
                                       bool[] interdites, bool[] affectee, TypeTerrain terrain, int nbGermes)
        {
            var classement = new List<int>(carte.NbZones);
            for (int z = 0; z < carte.NbZones; z++)
            {
                if (affectee[z] || !Autorise(p, interdites, z, terrain)) continue;
                classement.Add(z);
            }
            classement.Sort((a, b) =>
            {
                float aa = Aptitude(stats[a], terrain, p), ab = Aptitude(stats[b], terrain, p);
                if (aa != ab) return ab < aa ? -1 : 1;
                return a < b ? -1 : (a > b ? 1 : 0);
            });

            var germes = new List<int>(nbGermes);
            foreach (int candidat in classement)
            {
                if (germes.Count >= nbGermes) break;
                bool voisinDUnGerme = false;
                foreach (int germe in germes)
                {
                    if (carte.ZonesVoisines[candidat].Contains(germe)) { voisinDUnGerme = true; break; }
                }
                if (!voisinDUnGerme) germes.Add(candidat);
            }
            return germes;
        }

        static int CompterAffectees(bool[] affectee, int dejaComptees)
        {
            int n = 0;
            foreach (bool a in affectee)
            {
                if (a) n++;
            }
            return n - dejaComptees;
        }

        static IEnumerable<TypeTerrain> TerrainsPossibles(ParametresTerrains p, int zone, bool[] interdites)
        {
            yield return TypeTerrain.PlaineFertile;
            yield return TypeTerrain.PlaineArgileuse;
            yield return TypeTerrain.Foret;
            yield return TypeTerrain.Colline;
            yield return TypeTerrain.Cote;
            if (!interdites[zone])
            {
                yield return TypeTerrain.Desert;
                yield return TypeTerrain.Marais;
            }
        }

        static float Aptitude(StatistiqueZone s, TypeTerrain terrain, ParametresTerrains p)
        {
            switch (terrain)
            {
                case TypeTerrain.Cote:
                    return s.PartPlage >= p.PartPlageCote ? 2f + s.PartPlage : s.PartPlage;
                case TypeTerrain.Desert:
                    return 1.2f + s.Aridite - s.Humidite;
                case TypeTerrain.Marais:
                    return s.Elevation < 3.5f && s.Humidite > 0.7f ? 1.2f + s.Humidite : 0.2f * s.Humidite;
                case TypeTerrain.PlaineFertile:
                    return 1f + (1f - math.abs(s.Humidite - 0.55f)) - 0.05f * s.Elevation;
                case TypeTerrain.Foret:
                    return 0.9f + s.Humidite * 0.8f + 0.02f * s.Elevation;
                case TypeTerrain.Colline:
                    return 0.6f + 0.06f * s.Elevation + 0.5f * s.PartMassif;
                case TypeTerrain.PlaineArgileuse:
                    return 0.8f + 0.6f * s.Humidite - 0.04f * s.Elevation;
                default:
                    return 0f;
            }
        }

        static int VoisinesDuMemeTerrain(Carte carte, TypeTerrain[] terrains, bool[] affectee,
                                         int zone, TypeTerrain terrain)
        {
            int n = 0;
            foreach (int v in carte.ZonesVoisines[zone])
            {
                if (affectee[v] && terrains[v] == terrain) n++;
            }
            return n;
        }

        static int CompterVoisinagesSteriles(Carte carte, TypeTerrain[] terrains)
        {
            int steriles = 0;
            for (int z = 0; z < carte.NbZones; z++)
            {
                if (Terrains.Ressource(terrains[z]) != TypeRessource.Aucune) continue;
                int voisinesSteriles = 0;
                foreach (int v in carte.ZonesVoisines[z])
                {
                    if (Terrains.Ressource(terrains[v]) == TypeRessource.Aucune) voisinesSteriles++;
                }
                if (voisinesSteriles > 1) steriles++;
            }
            return steriles;
        }

        // ------------------------------------------------------------------- ponts

        /// <summary>
        /// Emplacements de pont : sur chaque frontière aquatique entre groupes qui n'a pas de
        /// passage, la portion la plus droite et la plus étroite. Un pont y ajoutera plus tard
        /// une liaison sans compter dans la limite de trois passages par groupe.
        /// </summary>
        static List<EmplacementPont> TrouverPonts(Carte carte)
        {
            GrapheCellules g = carte.Graphe;
            var ponts = new List<EmplacementPont>();
            var dejaVues = new HashSet<long>();

            foreach (SegmentFrontiere s in carte.Segments)
            {
                if (s.Type != TypeFrontiere.Riviere && s.Type != TypeFrontiere.RiviereProlongee) continue;
                if (s.Aretes.Length < 2) continue;

                long cle = ((long)math.min(s.GroupeA, s.GroupeB) << 32) | (uint)math.max(s.GroupeA, s.GroupeB);
                if (dejaVues.Contains(cle)) continue;

                bool aDejaUnPassage = false;
                foreach (Passage passage in carte.Passages)
                {
                    if ((passage.GroupeA == s.GroupeA && passage.GroupeB == s.GroupeB)
                     || (passage.GroupeA == s.GroupeB && passage.GroupeB == s.GroupeA))
                    {
                        aDejaUnPassage = true;
                        break;
                    }
                }
                if (aDejaUnPassage) continue;

                // Portion la plus droite : celle dont les deux arêtes forment l'angle le plus ouvert.
                int meilleur = -1;
                float meilleurCout = float.MaxValue;
                for (int i = 0; i + 1 < s.Aretes.Length; i++)
                {
                    float2 a = g.Coins[s.Coins[i]];
                    float2 b = g.Coins[s.Coins[i + 1]];
                    float2 c = g.Coins[s.Coins[i + 2]];
                    float2 u = math.normalizesafe(b - a);
                    float2 v = math.normalizesafe(c - b);
                    float alignement = math.dot(u, v);                       // 1 = parfaitement droit
                    float largeur = math.distance(a, c);
                    float cout = (1f - alignement) * 10f + largeur * 0.05f;
                    if (cout < meilleurCout) { meilleurCout = cout; meilleur = i; }
                }
                if (meilleur < 0) continue;

                dejaVues.Add(cle);
                ponts.Add(new EmplacementPont
                {
                    GroupeA = s.GroupeA,
                    GroupeB = s.GroupeB,
                    Aretes = new[] { s.Aretes[meilleur], s.Aretes[meilleur + 1] },
                    Position = g.Coins[s.Coins[meilleur + 1]],
                    Largeur = math.distance(g.Coins[s.Coins[meilleur]], g.Coins[s.Coins[meilleur + 2]])
                });
            }
            return ponts;
        }
    }
}
