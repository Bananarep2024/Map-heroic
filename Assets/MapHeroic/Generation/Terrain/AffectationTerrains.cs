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

        /// <summary>Prime accordée à un terrain déjà présent chez une voisine : forme les biomes.</summary>
        public float BonusContiguite = 0.15f;

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
                   $"({NbVoisinagesSansRessource} voisinages stériles), {NbPonts} emplacements de pont, " +
                   $"{Millisecondes} ms";
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

            // Couples (zone, terrain) classés par aptitude décroissante. Le bonus de
            // contiguïté est réévalué au moment de poser, pas au moment de classer : c'est
            // ce qui fait grandir les biomes de proche en proche.
            var candidats = new List<(int zone, TypeTerrain terrain, float aptitude)>(n * 6);
            for (int z = 0; z < n; z++)
            {
                if (affectee[z]) continue;
                foreach (TypeTerrain t in TerrainsPossibles(p, z, interdites))
                {
                    candidats.Add((z, t, Aptitude(stats[z], t, p)));
                }
            }
            candidats.Sort((a, b) =>
            {
                if (a.aptitude != b.aptitude) return b.aptitude < a.aptitude ? -1 : 1;
                if (a.zone != b.zone) return a.zone < b.zone ? -1 : 1;
                return (int)a.terrain - (int)b.terrain;
            });

            foreach (var (zone, terrain, aptitude) in candidats)
            {
                if (affectee[zone]) continue;
                int index = (int)terrain;
                if (quota[index] >= plafonds[index]) continue;

                float avecBonus = aptitude + p.BonusContiguite * VoisinesDuMemeTerrain(carte, terrains, affectee, zone, terrain);
                if (avecBonus < aptitude) continue;      // garde-fou, jamais atteint

                terrains[zone] = terrain;
                affectee[zone] = true;
                quota[index]++;
            }

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

            return terrains;
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
