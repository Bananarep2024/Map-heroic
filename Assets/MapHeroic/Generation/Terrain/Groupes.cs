using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;
using Unity.Mathematics;

namespace MapHeroic.Generation.Terrain
{
    public sealed class ParametresGroupes
    {
        public int TailleMin = 3;
        public int TailleMax = 4;

        public int IterationsRecuit = 6000;
        public float TemperatureDebut = 40f;
        public float TemperatureFin = 0.5f;

        /// <summary>
        /// Pénalité d'un groupe de taille interdite ou coupé en deux. Très au-dessus des
        /// termes de qualité, qui sont normalisés autour de la centaine : une partition
        /// invalide doit toujours coûter plus cher que la pire partition valide, sans quoi le
        /// recuit préférerait de jolies frontières à une découpe correcte.
        /// </summary>
        public float PoidsInvalide = 10000f;

        /// <summary>Frontière de groupe posée sur du terrain mou : mauvais.</summary>
        public float PoidsFrontiereMolle = 300f;

        /// <summary>Crête à l'intérieur d'un groupe : gênant.</summary>
        public float PoidsCreteInterne = 100f;

        /// <summary>Rivière à l'intérieur d'un groupe : très gênant, elle le coupera en deux.</summary>
        public float PoidsRiviereInterne = 400f;

        /// <summary>
        /// Groupe étiré plutôt que ramassé. Poids délibérément faible : ce terme vaut
        /// naturellement autour de 2,2 (une zone se tient à environ 1,5 rayon du centre de
        /// son groupe) là où les autres, normalisés par la longueur des frontières, valent
        /// quelques dixièmes. Au poids de 40 initialement retenu il représentait les trois
        /// quarts de l'énergie finale et le recuit optimisait la compacité au lieu du relief.
        /// </summary>
        public float PoidsEtalement = 15f;

        public int SansAmeliorationMax = 1500;
        public int RejeuxMax = 5;
    }

    public sealed class DiagnosticGroupes
    {
        public int Essais;
        public bool Reussi;
        public string MotifEchec;
        public int NbGroupes;
        public int NbGroupesDeTrois;
        public int NbGroupesDeQuatre;
        public int NbInvalidesApresGlouton;
        public int NbInvalidesFinal;
        public float EnergieInitiale;
        public float EnergieFinale;
        public int IterationsUtilisees;
        public float DureteFrontieresGroupes;
        public float DureteFrontieresInternes;

        /// <summary>Dureté moyenne de TOUTES les frontières de zones, groupées ou non.</summary>
        public float DureteFrontieresZones;
        public float LongueurRiviereInterne;
        public long Millisecondes;

        public override string ToString()
        {
            if (!Reussi) return $"groupes ÉCHEC après {Essais} essai(s) : {MotifEchec}";
            return $"{NbGroupes} groupes ({NbGroupesDeTrois}×3 + {NbGroupesDeQuatre}×4) en {Essais} essai(s) : " +
                   $"énergie {EnergieInitiale:F0} → {EnergieFinale:F0} en {IterationsUtilisees} itérations, " +
                   $"dureté {DureteFrontieresGroupes:F3} aux frontières de groupes contre " +
                   $"{DureteFrontieresInternes:F3} en interne (moyenne des frontières de zones : {DureteFrontieresZones:F3}), " +
                   $"{LongueurRiviereInterne:F0} m de rivière interne, {Millisecondes} ms";
        }
    }

    /// <summary>
    /// Phase P6 : regroupement des 90 zones en ensembles de 3 ou 4 zones adjacentes.
    ///
    /// Le problème n'a pas de solution évidente : il faut à la fois des tailles exactes, des
    /// groupes d'un seul tenant, et des frontières qui tombent là où le terrain résiste déjà.
    /// On procède donc en deux temps — une construction gloutonne qui part des zones les plus
    /// contraintes, puis un recuit simulé qui échange et déplace des zones frontalières.
    ///
    /// L'énergie mélange deux natures de termes. Les violations de structure (taille
    /// interdite, groupe coupé en deux) sont pénalisées cent fois plus cher que les termes de
    /// qualité, eux normalisés par la longueur totale des frontières. Sans cette séparation
    /// nette d'échelles, le recuit préférerait de belles frontières à une découpe correcte.
    ///
    /// Le recuit conserve la meilleure partition rencontrée et non la dernière : à
    /// température non nulle il accepte des dégradations, et rien ne garantit que son état
    /// final soit son meilleur.
    /// </summary>
    public static class Groupes
    {
        public const int Phase = 7;

        public static bool Construire(Carte carte, ParametresGroupes p, Rng rngRacine, out DiagnosticGroupes diag)
        {
            if (carte?.AretesEntreZones == null) throw new InvalidOperationException("Les zones doivent précéder les groupes.");

            var chrono = System.Diagnostics.Stopwatch.StartNew();
            diag = new DiagnosticGroupes();
            int nbZones = carte.NbZones;

            var centroides = CentroidesDeZones(carte);
            float longueurRef = 0f;
            foreach (AreteZones f in carte.AretesEntreZones) longueurRef += f.Longueur;
            if (longueurRef <= 0f) longueurRef = 1f;
            float rayonRef = RayonReference(carte);

            for (int essai = 0; essai < p.RejeuxMax; essai++)
            {
                diag = new DiagnosticGroupes { Essais = essai + 1 };
                var rng = rngRacine.Deriver(Phase * 100 + essai);

                int[] groupeDe = InitialiserGlouton(carte, p, centroides, ref rng, out int nbGroupes);
                var zonesDeGroupe = Regrouper(groupeDe, nbGroupes);
                diag.NbInvalidesApresGlouton = CompterInvalides(carte, zonesDeGroupe);

                diag.EnergieInitiale = Energie(carte, groupeDe, zonesDeGroupe, p, longueurRef,
                                               centroides, rayonRef, out _);

                Recuire(carte, p, groupeDe, nbGroupes, longueurRef, centroides, rayonRef, ref rng, diag);

                zonesDeGroupe = Regrouper(groupeDe, nbGroupes);
                diag.NbInvalidesFinal = CompterInvalides(carte, zonesDeGroupe);
                diag.EnergieFinale = Energie(carte, groupeDe, zonesDeGroupe, p, longueurRef,
                                             centroides, rayonRef, out _);

                if (diag.NbInvalidesFinal > 0)
                {
                    diag.MotifEchec = $"{diag.NbInvalidesFinal} groupe(s) de taille interdite ou coupé(s) en deux";
                    continue;
                }

                // Les groupes vidés par le recuit sont retirés et les index renumérotés.
                var compact = Compacter(groupeDe, zonesDeGroupe, out List<int>[] finales);
                carte.GroupeDeZone = compact;
                carte.ZonesDeGroupe = finales;
                carte.GroupesVoisins = ConstruireGrapheGroupes(carte, compact, finales.Length);

                diag.NbGroupes = finales.Length;
                foreach (List<int> zones in finales)
                {
                    if (zones.Count == 3) diag.NbGroupesDeTrois++;
                    else if (zones.Count == 4) diag.NbGroupesDeQuatre++;
                }

                MesurerFrontieres(carte, compact, diag);
                diag.Reussi = true;
                diag.Millisecondes = chrono.ElapsedMilliseconds;
                return true;
            }

            diag.Reussi = false;
            diag.Millisecondes = chrono.ElapsedMilliseconds;
            return false;
        }

        // --------------------------------------------------------------- construction

        /// <summary>
        /// Construction gloutonne partant à chaque fois de la zone la plus contrainte, celle
        /// qui a le moins de voisines encore libres. Commencer par les impasses évite de les
        /// laisser orphelines une fois tout le reste apparié.
        /// </summary>
        static int[] InitialiserGlouton(Carte carte, ParametresGroupes p, float2[] centroides,
                                        ref Rng rng, out int nbGroupes)
        {
            int n = carte.NbZones;
            var groupeDe = new int[n];
            for (int i = 0; i < n; i++) groupeDe[i] = -1;

            int restant = n;
            nbGroupes = 0;
            var membres = new List<int>(8);

            while (restant > 0)
            {
                int depart = -1;
                int moinsDeLibres = int.MaxValue;
                for (int z = 0; z < n; z++)
                {
                    if (groupeDe[z] >= 0) continue;
                    int libres = 0;
                    foreach (int v in carte.ZonesVoisines[z])
                    {
                        if (groupeDe[v] < 0) libres++;
                    }
                    if (libres < moinsDeLibres) { moinsDeLibres = libres; depart = z; }
                }
                if (depart < 0) break;

                int cible = ChoisirTaille(restant, p, ref rng);
                membres.Clear();
                membres.Add(depart);
                groupeDe[depart] = nbGroupes;

                while (membres.Count < cible)
                {
                    float2 centre = CentreDe(membres, centroides);
                    int meilleure = -1;
                    float meilleureDistance = float.MaxValue;

                    foreach (int m in membres)
                    {
                        foreach (int v in carte.ZonesVoisines[m])
                        {
                            if (groupeDe[v] >= 0) continue;
                            float dx = centroides[v].x - centre.x;
                            float dy = centroides[v].y - centre.y;
                            float d = dx * dx + dy * dy;
                            if (d < meilleureDistance || (d == meilleureDistance && v < meilleure))
                            {
                                meilleureDistance = d;
                                meilleure = v;
                            }
                        }
                    }

                    if (meilleure < 0) break;      // impasse : le groupe restera petit, le recuit tranchera
                    groupeDe[meilleure] = nbGroupes;
                    membres.Add(meilleure);
                }

                restant -= membres.Count;
                nbGroupes++;
            }

            return groupeDe;
        }

        /// <summary>
        /// Taille du prochain groupe. On ne retient que celles qui laissent un reste
        /// décomposable en 3 et 4 : tout entier sauf 1, 2 et 5.
        /// </summary>
        static int ChoisirTaille(int restant, ParametresGroupes p, ref Rng rng)
        {
            bool troisPossible = Decomposable(restant - p.TailleMin);
            bool quatrePossible = Decomposable(restant - p.TailleMax);

            if (troisPossible && quatrePossible) return rng.Chance(0.5f) ? p.TailleMin : p.TailleMax;
            if (quatrePossible) return p.TailleMax;
            if (troisPossible) return p.TailleMin;
            return Math.Min(restant, p.TailleMax);
        }

        static bool Decomposable(int m) => m == 0 || (m >= 3 && m != 5);

        // ---------------------------------------------------------------------- recuit

        static void Recuire(Carte carte, ParametresGroupes p, int[] groupeDe, int nbGroupes,
                            float longueurRef, float2[] centroides, float rayonRef,
                            ref Rng rng, DiagnosticGroupes diag)
        {
            var zonesDeGroupe = Regrouper(groupeDe, nbGroupes);
            float energie = Energie(carte, groupeDe, zonesDeGroupe, p, longueurRef, centroides, rayonRef, out int invalides);

            var meilleur = (int[])groupeDe.Clone();
            float meilleureEnergie = energie;
            int meilleurInvalides = invalides;
            int sansAmelioration = 0;

            for (int iteration = 0; iteration < p.IterationsRecuit; iteration++)
            {
                diag.IterationsUtilisees = iteration + 1;

                // Refroidissement linéaire : la décroissance géométrique demanderait une
                // puissance fractionnaire, donc un logarithme, qu'on s'interdit ici. À ce
                // nombre d'itérations, l'écart entre les deux ne se mesure pas.
                float progression = (float)iteration / p.IterationsRecuit;
                float temperature = p.TemperatureDebut + (p.TemperatureFin - p.TemperatureDebut) * progression;

                int z = rng.Entier(carte.NbZones);
                List<int> voisines = carte.ZonesVoisines[z];
                if (voisines.Count == 0) continue;
                int autre = voisines[rng.Entier(voisines.Count)];
                if (groupeDe[autre] == groupeDe[z]) continue;

                int groupeZ = groupeDe[z];
                int groupeAutre = groupeDe[autre];
                bool echange = rng.Chance(0.5f);

                if (echange)
                {
                    groupeDe[z] = groupeAutre;
                    groupeDe[autre] = groupeZ;
                }
                else
                {
                    groupeDe[z] = groupeAutre;
                }

                var apres = Regrouper(groupeDe, nbGroupes);
                float nouvelle = Energie(carte, groupeDe, apres, p, longueurRef, centroides, rayonRef, out int nouvInvalides);
                float delta = nouvelle - energie;

                bool accepte = delta < 0f || rng.Float01() < Tables.ExpNeg(delta / math.max(temperature, 1e-3f));
                if (accepte)
                {
                    energie = nouvelle;
                    invalides = nouvInvalides;
                    if (nouvelle < meilleureEnergie)
                    {
                        meilleureEnergie = nouvelle;
                        meilleurInvalides = nouvInvalides;
                        Array.Copy(groupeDe, meilleur, groupeDe.Length);
                        sansAmelioration = 0;
                    }
                    else sansAmelioration++;
                }
                else
                {
                    groupeDe[z] = groupeZ;
                    if (echange) groupeDe[autre] = groupeAutre;
                    sansAmelioration++;
                }

                if (meilleurInvalides == 0 && sansAmelioration > p.SansAmeliorationMax) break;
            }

            // Le recuit accepte des dégradations : son état final n'est pas forcément son meilleur.
            Array.Copy(meilleur, groupeDe, groupeDe.Length);
        }

        // -------------------------------------------------------------------- énergie

        static float Energie(Carte carte, int[] groupeDe, List<int>[] zonesDeGroupe, ParametresGroupes p,
                             float longueurRef, float2[] centroides, float rayonRef, out int invalides)
        {
            invalides = CompterInvalides(carte, zonesDeGroupe);
            float e = p.PoidsInvalide * invalides;

            double frontiereMolle = 0.0, creteInterne = 0.0, riviereInterne = 0.0;
            foreach (AreteZones f in carte.AretesEntreZones)
            {
                if (groupeDe[f.ZoneA] != groupeDe[f.ZoneB])
                {
                    frontiereMolle += f.Longueur * (1.0 - f.Durete);
                }
                else
                {
                    creteInterne += f.Longueur * f.Crete;
                    riviereInterne += f.LongueurRiviere;
                }
            }

            e += p.PoidsFrontiereMolle * (float)(frontiereMolle / longueurRef);
            e += p.PoidsCreteInterne * (float)(creteInterne / longueurRef);
            e += p.PoidsRiviereInterne * (float)(riviereInterne / longueurRef);

            double etalement = 0.0;
            int nbNonVides = 0;
            foreach (List<int> zones in zonesDeGroupe)
            {
                if (zones.Count == 0) continue;
                nbNonVides++;
                float2 centre = CentreDe(zones, centroides);
                double somme = 0.0;
                foreach (int z in zones)
                {
                    double dx = centroides[z].x - centre.x;
                    double dy = centroides[z].y - centre.y;
                    somme += (dx * dx + dy * dy) / (rayonRef * rayonRef);
                }
                etalement += somme / zones.Count;
            }
            if (nbNonVides > 0) e += p.PoidsEtalement * (float)(etalement / nbNonVides);

            return e;
        }

        static int CompterInvalides(Carte carte, List<int>[] zonesDeGroupe)
        {
            int invalides = 0;
            foreach (List<int> zones in zonesDeGroupe)
            {
                if (zones.Count == 0) continue;      // groupe vidé : sera renuméroté, pas une faute
                if (zones.Count < 3 || zones.Count > 4) invalides++;
                if (!EstConnexe(carte, zones)) invalides++;
            }
            return invalides;
        }

        const int TailleGroupeAberrante = 32;

        static bool EstConnexe(Carte carte, List<int> zones)
        {
            if (zones.Count <= 1) return true;

            // Le recuit peut, à haute température, gonfler un groupe bien au-delà de 4. Une
            // taille pareille est de toute façon pénalisée : inutile d'en vérifier la
            // connexité, et surtout pas au prix d'un dépassement de tampon.
            if (zones.Count > TailleGroupeAberrante) return false;

            int atteintes = 1;
            Span<int> pile = stackalloc int[TailleGroupeAberrante];
            Span<bool> vu = stackalloc bool[TailleGroupeAberrante];
            int sommet = 0;
            pile[sommet++] = 0;
            vu[0] = true;

            while (sommet > 0)
            {
                int i = pile[--sommet];
                foreach (int v in carte.ZonesVoisines[zones[i]])
                {
                    for (int j = 0; j < zones.Count; j++)
                    {
                        if (vu[j] || zones[j] != v) continue;
                        vu[j] = true;
                        atteintes++;
                        pile[sommet++] = j;
                    }
                }
            }
            return atteintes == zones.Count;
        }

        // ------------------------------------------------------------------ utilitaires

        static List<int>[] Regrouper(int[] groupeDe, int nbGroupes)
        {
            var listes = new List<int>[nbGroupes];
            for (int i = 0; i < nbGroupes; i++) listes[i] = new List<int>(4);
            for (int z = 0; z < groupeDe.Length; z++)
            {
                if (groupeDe[z] >= 0) listes[groupeDe[z]].Add(z);
            }
            return listes;
        }

        static int[] Compacter(int[] groupeDe, List<int>[] zonesDeGroupe, out List<int>[] finales)
        {
            var nouveauIndex = new int[zonesDeGroupe.Length];
            int suivant = 0;
            for (int i = 0; i < zonesDeGroupe.Length; i++)
            {
                nouveauIndex[i] = zonesDeGroupe[i].Count > 0 ? suivant++ : -1;
            }

            var resultat = new int[groupeDe.Length];
            for (int z = 0; z < groupeDe.Length; z++) resultat[z] = nouveauIndex[groupeDe[z]];

            finales = new List<int>[suivant];
            for (int i = 0, k = 0; i < zonesDeGroupe.Length; i++)
            {
                if (zonesDeGroupe[i].Count > 0) finales[k++] = zonesDeGroupe[i];
            }
            return resultat;
        }

        public static List<int>[] ConstruireGrapheGroupes(Carte carte, int[] groupeDe, int nbGroupes)
        {
            var voisins = new List<int>[nbGroupes];
            for (int i = 0; i < nbGroupes; i++) voisins[i] = new List<int>(6);

            var vues = new HashSet<long>();
            foreach (AreteZones f in carte.AretesEntreZones)
            {
                int ga = groupeDe[f.ZoneA];
                int gb = groupeDe[f.ZoneB];
                if (ga == gb) continue;
                long cle = ga < gb ? ((long)ga << 32) | (uint)gb : ((long)gb << 32) | (uint)ga;
                if (!vues.Add(cle)) continue;
                voisins[ga].Add(gb);
                voisins[gb].Add(ga);
            }
            for (int i = 0; i < nbGroupes; i++) voisins[i].Sort();
            return voisins;
        }

        static float2[] CentroidesDeZones(Carte carte)
        {
            var centroides = new float2[carte.NbZones];
            for (int z = 0; z < carte.NbZones; z++)
            {
                double sx = 0.0, sy = 0.0, poids = 0.0;
                foreach (int c in carte.CellulesDeZone[z])
                {
                    double a = carte.Graphe.Aire[c];
                    sx += carte.Graphe.Sites[c].x * a;
                    sy += carte.Graphe.Sites[c].y * a;
                    poids += a;
                }
                centroides[z] = poids > 0.0 ? new float2((float)(sx / poids), (float)(sy / poids)) : float2.zero;
            }
            return centroides;
        }

        static float RayonReference(Carte carte)
        {
            double aire = 0.0;
            for (int z = 0; z < carte.NbZones; z++)
            {
                foreach (int c in carte.CellulesDeZone[z]) aire += carte.Graphe.Aire[c];
            }
            double moyenne = aire / Math.Max(1, carte.NbZones);
            return (float)Math.Sqrt(moyenne / Math.PI);
        }

        static float2 CentreDe(List<int> zones, float2[] centroides)
        {
            float sx = 0f, sy = 0f;
            foreach (int z in zones) { sx += centroides[z].x; sy += centroides[z].y; }
            return new float2(sx / zones.Count, sy / zones.Count);
        }

        static void MesurerFrontieres(Carte carte, int[] groupeDe, DiagnosticGroupes diag)
        {
            double externe = 0.0, longueurExterne = 0.0;
            double interne = 0.0, longueurInterne = 0.0;
            double riviere = 0.0;

            foreach (AreteZones f in carte.AretesEntreZones)
            {
                if (groupeDe[f.ZoneA] != groupeDe[f.ZoneB])
                {
                    externe += f.Durete * f.Longueur;
                    longueurExterne += f.Longueur;
                }
                else
                {
                    interne += f.Durete * f.Longueur;
                    longueurInterne += f.Longueur;
                    riviere += f.LongueurRiviere;
                }
            }

            diag.DureteFrontieresGroupes = longueurExterne > 0.0 ? (float)(externe / longueurExterne) : 0f;
            diag.DureteFrontieresInternes = longueurInterne > 0.0 ? (float)(interne / longueurInterne) : 0f;
            double toutes = externe + interne;
            double longueurTotale = longueurExterne + longueurInterne;
            diag.DureteFrontieresZones = longueurTotale > 0.0 ? (float)(toutes / longueurTotale) : 0f;
            diag.LongueurRiviereInterne = (float)riviere;
        }
    }
}
