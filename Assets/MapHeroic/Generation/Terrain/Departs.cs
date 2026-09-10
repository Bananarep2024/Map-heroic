using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;
using Unity.Mathematics;

namespace MapHeroic.Generation.Terrain
{
    public sealed class ParametresDeparts
    {
        public int NbJoueursMax = 6;

        /// <summary>Part constructible minimale d'une zone de départ.</summary>
        public float PartConstructibleMin = 0.75f;

        /// <summary>Part de massif maximale d'une zone de départ.</summary>
        public float PartMassifMax = 0.20f;

        /// <summary>Distance minimale entre deux départs, en nombre de passages franchis.</summary>
        public int DistanceGrapheMin = 3;

        /// <summary>Repli si la contrainte précédente ne laisse pas six candidats.</summary>
        public int DistanceGrapheRepli = 2;

        /// <summary>
        /// Distance euclidienne minimale, exprimée en fraction du rayon qu'aurait chacun des
        /// six territoires si l'île était partagée en disques égaux. Relative et non absolue :
        /// un seuil en mètres ne voudrait plus rien dire si la taille de carte changeait.
        /// </summary>
        public float FractionDistanceMin = 0.55f;

        /// <summary>Écart maximal toléré entre le meilleur et le pire départ.</summary>
        public float EcartScoreMax = 0.20f;

        /// <summary>Candidats essayés comme point d'ancrage avant d'abandonner.</summary>
        public int AncragesEssayes = 24;

        public int EssaisEchange = 10;
        public int RejeuxMax = 5;
    }

    public sealed class DiagnosticDeparts
    {
        public int Essais;
        public bool Reussi;
        public string MotifEchec;
        public int NbCandidats;
        public int DistanceGrapheMin;
        public float DistanceEuclidienneMin;
        public float SeuilDistance;
        public float EcartScore;
        public string EchelonUtilise = "";

        /// <summary>L'écart de score tient-il dans la cible ? Mesure de qualité, pas critère de rejet.</summary>
        public bool CibleAtteinte;

        /// <summary>Taille des groupes retenus (0 = tailles mélangées).</summary>
        public int TailleGroupeDepart;
        public float ScoreMin;
        public float ScoreMax;
        public long Millisecondes;

        public override string ToString()
        {
            if (!Reussi) return $"départs ÉCHEC après {Essais} essai(s) : {MotifEchec}";
            return $"6 départs sur {NbCandidats} candidats en {Essais} essai(s) : " +
                   $"{DistanceGrapheMin} passages et {DistanceEuclidienneMin:F0} m d'écart minimum " +
                   $"(seuil {SeuilDistance:F0} m), scores {ScoreMin:F2} à {ScoreMax:F2} " +
                   $"(écart {EcartScore:P0}{(CibleAtteinte ? "" : ", HORS CIBLE")}) ; groupes de {(TailleGroupeDepart > 0 ? TailleGroupeDepart.ToString() : "taille mêlée")}, " +
                   $"échelon « {EchelonUtilise} », {Millisecondes} ms";
        }
    }

    /// <summary>
    /// Phase P9 : choix des six groupes de départ.
    ///
    /// Deux exigences se contredisent. L'équité demande des positions comparables ; la
    /// lisibilité stratégique demande qu'elles soient éloignées les unes des autres. On
    /// résout d'abord l'éloignement, par sélection du point le plus distant sur le graphe des
    /// groupes, puis on corrige l'équité par échanges avec les candidats suivants.
    ///
    /// Les départs sont choisis AVANT les terrains, contrairement à l'ordre qu'on pourrait
    /// croire naturel. Ainsi la contrainte « pas de désert ni de marais autour d'un départ »
    /// s'applique en une seule passe d'affectation, au lieu de rejouer les terrains jusqu'à
    /// ce qu'ils tombent juste.
    /// </summary>
    public static class Departs
    {
        public const int Phase = 10;

        public static bool Construire(Carte carte, ParametresDeparts p, Rng rngRacine, out DiagnosticDeparts diag)
        {
            if (carte?.ZoneMontagne == null) throw new InvalidOperationException("La matérialisation doit précéder les départs.");

            var chrono = System.Diagnostics.Stopwatch.StartNew();
            diag = new DiagnosticDeparts();

            float aireIle = 0f;
            for (int c = 0; c < carte.NbCellules; c++)
            {
                if (carte.Terre[c]) aireIle += carte.Graphe.Aire[c];
            }
            float seuilDistance = p.FractionDistanceMin * math.sqrt(aireIle / p.NbJoueursMax);
            diag.SeuilDistance = seuilDistance;

            var candidats = Candidats(carte, p, diag);
            diag.NbCandidats = candidats.Count;
            if (candidats.Count < p.NbJoueursMax)
            {
                diag.MotifEchec = $"seulement {candidats.Count} groupes candidats pour {p.NbJoueursMax} départs";
                diag.Millisecondes = chrono.ElapsedMilliseconds;
                return false;
            }

            float2[] centres = CentresDeGroupes(carte);
            int[,] distances = DistancesEntreGroupes(carte);
            Dictionary<int, float> scores = Scores(carte, candidats);

            // Échelle de relâchement explicite. Aucune combinaison ne convient à toutes les
            // îles : une île étirée éloigne facilement six départs, une île ramassée non. On
            // descend d'un cran à la fois et l'on note lequel a servi, plutôt que de choisir
            // d'emblée une contrainte assez lâche pour ne jamais échouer.
            var echelons = new (int passages, float fraction)[]
            {
                (p.DistanceGrapheMin, 1.0f),
                (p.DistanceGrapheMin, 0.8f),
                (p.DistanceGrapheRepli, 0.8f),
                (p.DistanceGrapheRepli, 0.6f),
                (1, 0.6f),
                (1, 0.4f),

                // Dernier recours : aucune contrainte d'éloignement. Des départs rapprochés
                // font une carte discutable, aucune carte n'en fait aucune.
                (0, 0f)
            };

            // On parcourt toute l'échelle et l'on garde l'arrangement le PLUS ÉQUITABLE, au
            // lieu de rejeter tout ce qui dépasse un seuil.
            //
            // L'écart de score entre six départs a un plancher que le placement ne peut pas
            // franchir : les zones font déjà ± 25 % d'aire, et la part non constructible varie
            // de 0 à 43 % d'un groupe à l'autre. La cible de ± 20 % du document supposait ce
            // plancher plus bas qu'il n'est ; refuser tout ce qui la dépasse condamnait deux
            // cartes sur trois alors qu'elles étaient parfaitement jouables. On rend donc le
            // meilleur arrangement possible pour cette île, et l'on publie son écart : c'est
            // une mesure de qualité, pas un critère de rejet.
            List<int> meilleurChoix = null;
            float meilleurEcart = float.MaxValue;
            string meilleurEchelon = "";

            foreach (var (passages, fraction) in echelons)
            {
                float seuil = seuilDistance * fraction;
                List<int> choix = Selectionner(candidats, centres, distances, scores, p, seuil, passages);
                if (choix == null) continue;

                EgaliserScores(candidats, choix, centres, distances, scores, p, seuil, passages);
                float ecart = Ecart(choix, scores);
                if (ecart < meilleurEcart)
                {
                    meilleurEcart = ecart;
                    meilleurChoix = new List<int>(choix);
                    meilleurEchelon = $"{passages} passages, {fraction:P0} de la distance";
                }

                // Inutile de relâcher davantage si l'on tient déjà la cible.
                if (ecart <= p.EcartScoreMax) break;
            }

            diag.Essais = 1;
            if (meilleurChoix == null)
            {
                diag.MotifEchec = "impossible de placer six départs, même au dernier échelon";
                diag.Millisecondes = chrono.ElapsedMilliseconds;
                return false;
            }

            MesurerChoix(meilleurChoix, centres, distances, scores, diag);
            diag.EchelonUtilise = meilleurEchelon;
            diag.CibleAtteinte = diag.EcartScore <= p.EcartScoreMax;

            carte.GroupesDepart = meilleurChoix.ToArray();
            carte.ZonesDepart = new int[meilleurChoix.Count];
            for (int i = 0; i < meilleurChoix.Count; i++)
            {
                carte.ZonesDepart[i] = ZoneDeDepart(carte, p, meilleurChoix[i]);
            }

            diag.Reussi = true;
            diag.Millisecondes = chrono.ElapsedMilliseconds;
            return true;
        }

        // ----------------------------------------------------------------- candidats

        /// <summary>
        /// Candidats au départ, à taille de groupe égale.
        ///
        /// Un groupe de trois zones offre un quart de territoire de moins qu'un groupe de
        /// quatre : tant que les deux sont éligibles, l'écart de score ne peut pas descendre
        /// sous 25 %, quel que soit le soin mis à choisir. On ne retient donc qu'une seule
        /// taille — la plus grande d'abord, puisqu'elle laisse le plus de marge — et l'on ne
        /// mélange que si aucune ne fournit six candidats.
        /// </summary>
        static List<int> Candidats(Carte carte, ParametresDeparts p, DiagnosticDeparts diag)
        {
            var parTaille = new Dictionary<int, List<int>>();
            var toutes = new List<int>();

            for (int g = 0; g < carte.NbGroupes; g++)
            {
                int degre = carte.GroupesVoisins[g].Count;
                if (degre < 2 || degre > 3) continue;
                if (ZoneDeDepart(carte, p, g) < 0) continue;

                int taille = carte.ZonesDeGroupe[g].Count;
                if (!parTaille.TryGetValue(taille, out List<int> liste))
                {
                    liste = new List<int>();
                    parTaille[taille] = liste;
                }
                liste.Add(g);
                toutes.Add(g);
            }

            for (int taille = 4; taille >= 3; taille--)
            {
                if (parTaille.TryGetValue(taille, out List<int> liste) && liste.Count >= p.NbJoueursMax)
                {
                    diag.TailleGroupeDepart = taille;
                    return liste;
                }
            }

            // Aucune taille ne suffit seule : on mélange, quitte à accepter un écart plus large.
            diag.TailleGroupeDepart = 0;
            if (toutes.Count < p.NbJoueursMax)
            {
                // Dernier recours : on lève la contrainte de degré.
                toutes.Clear();
                for (int g = 0; g < carte.NbGroupes; g++)
                {
                    if (ZoneDeDepart(carte, p, g) >= 0) toutes.Add(g);
                }
            }
            return toutes;
        }

        /// <summary>
        /// Zone du groupe où le joueur posera sa première construction : la plus constructible
        /// possible, sans lac ni montagne. Renvoie -1 si le groupe n'en a aucune.
        /// </summary>
        public static int ZoneDeDepart(Carte carte, ParametresDeparts p, int groupe)
        {
            int meilleure = -1;
            float meilleurePart = 0f;

            foreach (int z in carte.ZonesDeGroupe[groupe])
            {
                if (carte.ZoneMontagne[z]) continue;

                int total = carte.CellulesDeZone[z].Count;
                int constructibles = 0, massif = 0, lac = 0;
                foreach (int c in carte.CellulesDeZone[z])
                {
                    if (!carte.ADrapeau(c, DrapeauxCellule.NonConstructible)) constructibles++;
                    if (carte.ADrapeau(c, DrapeauxCellule.Massif)) massif++;
                    if (carte.ADrapeau(c, DrapeauxCellule.Lac)) lac++;
                }

                if (lac > 0) continue;
                float part = (float)constructibles / total;
                if (part < p.PartConstructibleMin) continue;
                if ((float)massif / total > p.PartMassifMax) continue;

                if (part > meilleurePart || (part == meilleurePart && z < meilleure))
                {
                    meilleurePart = part;
                    meilleure = z;
                }
            }
            return meilleure;
        }

        /// <summary>
        /// Scores normalisés sur l'ensemble des candidats : chaque composante est ramenée à
        /// sa valeur maximale, puis pondérée.
        ///
        /// La première version additionnait des grandeurs brutes — des mètres carrés, des
        /// compteurs de sockets — et les compteurs discrets dominaient tout : deux mines de
        /// différence pesaient plus que la surface bâtissable entière, et l'écart entre
        /// départs atteignait 77 %. Normalisées, les composantes deviennent comparables et
        /// c'est bien la surface qui pèse le plus, comme il se doit.
        /// </summary>
        static Dictionary<int, float> Scores(Carte carte, List<int> candidats)
        {
            var aire = new Dictionary<int, float>();
            var mines = new Dictionary<int, int>();
            var peche = new Dictionary<int, int>();

            float aireMax = 1f;
            int minesMax = 1, pecheMax = 1;

            foreach (int groupe in candidats)
            {
                float a = 0f;
                int m = 0, pe = 0;
                foreach (int z in carte.ZonesDeGroupe[groupe])
                {
                    foreach (int c in carte.CellulesDeZone[z])
                    {
                        if (carte.ADrapeau(c, DrapeauxCellule.NonConstructible)) continue;
                        a += carte.Graphe.Aire[c];
                        if (carte.ADrapeau(c, DrapeauxCellule.SocketMine)) m++;
                        if (carte.ADrapeau(c, DrapeauxCellule.SocketPeche)) pe++;
                    }
                }
                aire[groupe] = a;
                mines[groupe] = m;
                peche[groupe] = pe;
                aireMax = math.max(aireMax, a);
                minesMax = math.max(minesMax, m);
                pecheMax = math.max(pecheMax, pe);
            }

            var scores = new Dictionary<int, float>(candidats.Count);
            foreach (int groupe in candidats)
            {
                scores[groupe] = 1.00f * (aire[groupe] / aireMax)
                               + 0.30f * (mines[groupe] / (float)minesMax)
                               + 0.20f * (peche[groupe] / (float)pecheMax)
                               + 0.10f * (carte.GroupesVoisins[groupe].Count / 3f);
            }
            return scores;
        }

        // ---------------------------------------------------------------- sélection

        static List<int> Selectionner(List<int> candidats, float2[] centres, int[,] distances,
                                      Dictionary<int, float> scores, ParametresDeparts p,
                                      float seuilDistance, int distanceGrapheMin)
        {
            // Les ancrages sont essayés du plus MÉDIAN au plus extrême, et non du mieux noté
            // au moins bon. Ancrer sur le meilleur candidat garantissait que le maximum de la
            // série soit un extrême, donc un écart large quoi qu'on fasse ensuite ; partir du
            // milieu laisse la place de resserrer des deux côtés.
            var tries = new List<float>(scores.Count);
            foreach (int g in candidats) tries.Add(scores[g]);
            tries.Sort();
            float mediane = tries[tries.Count / 2];

            var ancrages = new List<int>(candidats);
            ancrages.Sort((a, b) =>
            {
                float ea = math.abs(scores[a] - mediane), eb = math.abs(scores[b] - mediane);
                if (ea != eb) return ea < eb ? -1 : 1;
                return a < b ? -1 : (a > b ? 1 : 0);
            });

            int essais = math.min(p.AncragesEssayes, ancrages.Count);
            for (int k = 0; k < essais; k++)
            {
                var choix = new List<int> { ancrages[k] };

                while (choix.Count < p.NbJoueursMax)
                {
                    int meilleur = -1;
                    float meilleureDistance = -1f;

                    foreach (int g in candidats)
                    {
                        if (choix.Contains(g)) continue;
                        if (!Acceptable(g, choix, centres, distances, seuilDistance, distanceGrapheMin)) continue;

                        float minDistance = float.MaxValue;
                        foreach (int autre in choix)
                        {
                            minDistance = math.min(minDistance, math.distance(centres[g], centres[autre]));
                        }
                        if (minDistance > meilleureDistance || (minDistance == meilleureDistance && g < meilleur))
                        {
                            meilleureDistance = minDistance;
                            meilleur = g;
                        }
                    }

                    if (meilleur < 0) break;
                    choix.Add(meilleur);
                }

                if (choix.Count == p.NbJoueursMax) return choix;
            }
            return null;
        }

        static bool Acceptable(int candidat, List<int> choix, float2[] centres, int[,] distances,
                               float seuilDistance, int distanceGrapheMin)
        {
            foreach (int autre in choix)
            {
                if (distances[candidat, autre] < distanceGrapheMin) return false;
                if (math.distance(centres[candidat], centres[autre]) < seuilDistance) return false;
            }
            return true;
        }

        /// <summary>Remplace le plus mauvais départ tant qu'un échange resserre l'écart de scores.</summary>
        static void EgaliserScores(List<int> candidats, List<int> choix, float2[] centres, int[,] distances,
                                   Dictionary<int, float> scores, ParametresDeparts p,
                                   float seuilDistance, int distanceGrapheMin)
        {
            for (int essai = 0; essai < p.EssaisEchange; essai++)
            {
                // On tente de remplacer les DEUX extrêmes. Ne s'en prendre qu'au plus faible
                // laissait le plus fort tirer l'écart vers le haut sans jamais être touché.
                int pire = 0, meilleurIndex = 0;
                for (int i = 1; i < choix.Count; i++)
                {
                    if (scores[choix[i]] < scores[choix[pire]]) pire = i;
                    if (scores[choix[i]] > scores[choix[meilleurIndex]]) meilleurIndex = i;
                }

                float ecartAvant = Ecart(choix, scores);
                int positionRetenue = -1, remplacantRetenu = -1;
                float meilleurEcart = ecartAvant;

                foreach (int position in new[] { pire, meilleurIndex })
                {
                    int remplace = choix[position];
                    foreach (int g in candidats)
                    {
                        if (choix.Contains(g)) continue;
                        choix[position] = g;

                        bool ok = true;
                        for (int i = 0; i < choix.Count && ok; i++)
                        {
                            if (i == position) continue;
                            if (distances[g, choix[i]] < distanceGrapheMin) ok = false;
                            else if (math.distance(centres[g], centres[choix[i]]) < seuilDistance) ok = false;
                        }
                        if (ok)
                        {
                            float ecart = Ecart(choix, scores);
                            if (ecart < meilleurEcart)
                            {
                                meilleurEcart = ecart;
                                positionRetenue = position;
                                remplacantRetenu = g;
                            }
                        }
                        choix[position] = remplace;
                    }
                }

                if (positionRetenue < 0) break;
                choix[positionRetenue] = remplacantRetenu;
            }
        }

        static float Ecart(List<int> choix, Dictionary<int, float> scores)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (int g in choix)
            {
                min = math.min(min, scores[g]);
                max = math.max(max, scores[g]);
            }
            return max > 0f ? (max - min) / max : 0f;
        }

        static void MesurerChoix(List<int> choix, float2[] centres, int[,] distances,
                                 Dictionary<int, float> scores, DiagnosticDeparts diag)
        {
            diag.DistanceGrapheMin = int.MaxValue;
            diag.DistanceEuclidienneMin = float.MaxValue;
            diag.ScoreMin = float.MaxValue;
            diag.ScoreMax = float.MinValue;

            for (int i = 0; i < choix.Count; i++)
            {
                diag.ScoreMin = math.min(diag.ScoreMin, scores[choix[i]]);
                diag.ScoreMax = math.max(diag.ScoreMax, scores[choix[i]]);
                for (int j = i + 1; j < choix.Count; j++)
                {
                    diag.DistanceGrapheMin = math.min(diag.DistanceGrapheMin, distances[choix[i], choix[j]]);
                    diag.DistanceEuclidienneMin = math.min(diag.DistanceEuclidienneMin,
                                                           math.distance(centres[choix[i]], centres[choix[j]]));
                }
            }
            diag.EcartScore = Ecart(choix, scores);
        }

        // --------------------------------------------------------------- géométrie

        static float2[] CentresDeGroupes(Carte carte)
        {
            var centres = new float2[carte.NbGroupes];
            for (int g = 0; g < carte.NbGroupes; g++)
            {
                double sx = 0.0, sy = 0.0, poids = 0.0;
                foreach (int z in carte.ZonesDeGroupe[g])
                {
                    foreach (int c in carte.CellulesDeZone[z])
                    {
                        double a = carte.Graphe.Aire[c];
                        sx += carte.Graphe.Sites[c].x * a;
                        sy += carte.Graphe.Sites[c].y * a;
                        poids += a;
                    }
                }
                centres[g] = poids > 0.0 ? new float2((float)(sx / poids), (float)(sy / poids)) : float2.zero;
            }
            return centres;
        }

        /// <summary>
        /// Distances en nombre de passages franchis. On parcourt le graphe des PASSAGES et non
        /// celui des voisinages : deux groupes mitoyens séparés par une chaîne de montagnes
        /// sont loin l'un de l'autre pour un joueur, quoi qu'en dise la géométrie.
        /// </summary>
        static int[,] DistancesEntreGroupes(Carte carte)
        {
            int n = carte.NbGroupes;
            var adjacence = new List<int>[n];
            for (int i = 0; i < n; i++) adjacence[i] = new List<int>(4);
            foreach (Passage passage in carte.Passages)
            {
                adjacence[passage.GroupeA].Add(passage.GroupeB);
                adjacence[passage.GroupeB].Add(passage.GroupeA);
            }

            var distances = new int[n, n];
            var file = new int[n];

            for (int depart = 0; depart < n; depart++)
            {
                for (int i = 0; i < n; i++) distances[depart, i] = int.MaxValue;
                distances[depart, depart] = 0;

                int tete = 0, queue = 0;
                file[queue++] = depart;
                while (tete < queue)
                {
                    int g = file[tete++];
                    foreach (int v in adjacence[g])
                    {
                        if (distances[depart, v] != int.MaxValue) continue;
                        distances[depart, v] = distances[depart, g] + 1;
                        file[queue++] = v;
                    }
                }
            }
            return distances;
        }
    }
}
