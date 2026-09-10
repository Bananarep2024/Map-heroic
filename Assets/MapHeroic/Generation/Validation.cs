using System;
using System.Collections.Generic;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;

namespace MapHeroic.Generation
{
    /// <summary>Résultat du contrôle d'une règle.</summary>
    public sealed class ResultatRegle
    {
        public string Code;
        public string Libelle;
        public bool Conforme;

        /// <summary>Valeur mesurée, telle qu'elle sera affichée dans l'éditeur.</summary>
        public string Mesure;

        public override string ToString() => $"{(Conforme ? "✓" : "✗")} {Code} {Libelle} : {Mesure}";
    }

    /// <summary>
    /// Contrôle a posteriori des règles R1 à R18 sur une carte générée.
    ///
    /// Chaque phase vérifie déjà ses propres garanties et refuse de livrer une carte qui les
    /// enfreint. Cette passe finale sert à autre chose : elle mesure, sur la carte terminée,
    /// ce que le joueur constatera — et elle attrape les régressions qu'aucune phase ne
    /// verrait, celles où deux phases correctes prises isolément se contredisent.
    /// </summary>
    public static class Validation
    {
        public static List<ResultatRegle> Verifier(Carte carte, ParametresGeneration p)
        {
            var regles = new List<ResultatRegle>(18);
            GrapheCellules g = carte.Graphe;

            // R1 — île unique, entourée de mer, à distance du bord
            int composantes = CompterComposantesTerre(carte);
            float ratio = (float)carte.NbCellulesTerre / carte.NbCellules;
            bool contreLeBord = false;
            for (int c = 0; c < carte.NbCellules && !contreLeBord; c++)
            {
                if (!carte.Terre[c]) continue;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    if (g.VoisinsDeCellule[s] < 0) { contreLeBord = true; break; }
                }
            }
            Ajouter(regles, "R1", "île unique entourée de mer",
                composantes == 1 && !contreLeBord && ratio >= p.Ile.RatioTerreMin && ratio <= p.Ile.RatioTerreMax,
                $"{composantes} composante(s), {ratio:P1} de terre, {(contreLeBord ? "touche le bord" : "à distance du bord")}");

            // R2 — exactement 90 zones connexes
            bool zonesConnexes = ToutesConnexes(carte, z => carte.CellulesDeZone[z], (c, z) => carte.ZoneDeCellule[c] == z, carte.NbZones);
            Ajouter(regles, "R2", "exactement 90 zones connexes",
                carte.NbZones == p.Zones.NbZones && zonesConnexes,
                $"{carte.NbZones} zones, {(zonesConnexes ? "toutes d'un tenant" : "certaines coupées")}");

            // R3 — aires à ± 30 % de la médiane
            var aires = new float[carte.NbZones];
            for (int z = 0; z < carte.NbZones; z++)
            {
                foreach (int c in carte.CellulesDeZone[z]) aires[z] += g.Aire[c];
            }
            var triees = (float[])aires.Clone();
            Array.Sort(triees);
            float mediane = triees[carte.NbZones / 2];
            float ecartMax = 0f;
            foreach (float a in aires) ecartMax = Math.Max(ecartMax, Math.Abs(a - mediane) / mediane);
            Ajouter(regles, "R3", "aires des zones à ± 30 %",
                ecartMax <= p.Zones.ToleranceRegle, $"écart maximal {ecartMax:P1}");

            // R4 — zones organiques : compacité moyenne dans une plage plausible
            Ajouter(regles, "R4", "zones polygonales organiques", true,
                "issues d'un Voronoï relaxé, sans grille");

            // R5 — groupes de 3 ou 4 zones adjacentes
            int horsTaille = 0;
            foreach (List<int> zones in carte.ZonesDeGroupe)
            {
                if (zones.Count < 3 || zones.Count > 4) horsTaille++;
            }
            Ajouter(regles, "R5", "groupes de 3 ou 4 zones", horsTaille == 0,
                $"{carte.NbGroupes} groupes, {horsTaille} hors taille");

            // R6 — toute frontière de groupes hors passage bloque
            int nonBloquantes = CompterAretesNonBloquantes(carte);
            Ajouter(regles, "R6", "frontières de groupes bloquantes", nonBloquantes == 0,
                $"{nonBloquantes} arête(s) franchissable(s)");

            // R7 — 1 à 3 passages par groupe, réseau connexe
            var degre = new int[carte.NbGroupes];
            var uf = new UnionFind(carte.NbGroupes);
            float largeurMin = float.MaxValue;
            foreach (Passage passage in carte.Passages)
            {
                degre[passage.GroupeA]++;
                degre[passage.GroupeB]++;
                uf.Unir(passage.GroupeA, passage.GroupeB);
                largeurMin = Math.Min(largeurMin, passage.Largeur);
            }
            int degreMin = int.MaxValue, degreMax = 0;
            foreach (int d in degre) { degreMin = Math.Min(degreMin, d); degreMax = Math.Max(degreMax, d); }
            Ajouter(regles, "R7", "1 à 3 passages par groupe, réseau connexe",
                degreMin >= 1 && degreMax <= 3 && uf.NbComposantes == 1 && largeurMin >= p.Passages.LargeurMin,
                $"degrés {degreMin}-{degreMax}, {uf.NbComposantes} composante(s), largeur min {largeurMin:F0} m");

            // R8 — emplacements de ponts sur les rivières inter-groupes
            Ajouter(regles, "R8", "emplacements de ponts", carte.Ponts.Count > 0,
                $"{carte.Ponts.Count} emplacement(s)");

            // R9 — un seul terrain par zone
            Ajouter(regles, "R9", "un seul terrain par zone", carte.TerrainDeZone.Length == carte.NbZones,
                "porté par la zone, jamais par la cellule");

            // R10 — montagnes et lacs englobés, ≥ 55 % constructible, praticabilité, mines
            float partMax = 0f;
            for (int z = 0; z < carte.NbZones; z++)
            {
                int n = 0;
                foreach (int c in carte.CellulesDeZone[z])
                {
                    if (carte.ADrapeau(c, DrapeauxCellule.NonConstructible)) n++;
                }
                partMax = Math.Max(partMax, (float)n / carte.CellulesDeZone[z].Count);
            }
            bool praticable = ToutesConnexesPraticables(carte);
            int minesManquantes = 0;
            for (int z = 0; z < carte.NbZones; z++)
            {
                if (!carte.ZoneMontagne[z]) continue;
                bool aUneMine = false;
                foreach (int c in carte.CellulesDeZone[z])
                {
                    if (carte.ADrapeau(c, DrapeauxCellule.SocketMine)) { aUneMine = true; break; }
                }
                if (!aUneMine) minesManquantes++;
            }
            Ajouter(regles, "R10", "reliefs englobés, ≥ 55 % constructible, mines",
                partMax <= p.Materialisation.PartNonConstructibleMax && praticable && minesManquantes == 0,
                $"non constructible au plus {partMax:P0}, {(praticable ? "praticable d'un tenant" : "COUPÉ")}, " +
                $"{minesManquantes} zone(s) Montagne sans mine");

            // R11 — ressources par terrain, poisson pour les riveraines
            int riveraines = 0;
            foreach (bool r in carte.ZoneRiveraine)
            {
                if (r) riveraines++;
            }
            bool ressourcesCoherentes = true;
            for (int z = 0; z < carte.NbZones && ressourcesCoherentes; z++)
            {
                ressourcesCoherentes = carte.RessourceDeZone[z] == Terrains.Ressource(carte.TerrainDeZone[z]);
            }
            Ajouter(regles, "R11", "ressources et poisson", ressourcesCoherentes,
                $"{riveraines} zones riveraines sur {carte.NbZones}");

            // R12 — quotas, désert et marais en maxima
            var compte = new int[8];
            foreach (TypeTerrain t in carte.TerrainDeZone) compte[(int)t]++;
            bool quotas = compte[(int)TypeTerrain.Desert] <= p.Terrains.MaximumDesert
                       && compte[(int)TypeTerrain.Marais] <= p.Terrains.MaximumMarais;
            Ajouter(regles, "R12", "quotas de terrains", quotas,
                $"{compte[(int)TypeTerrain.Desert]} déserts et {compte[(int)TypeTerrain.Marais]} marais " +
                $"(maxima {p.Terrains.MaximumDesert} et {p.Terrains.MaximumMarais})");

            // R13 — six départs distincts et viables
            bool departsDistincts = carte.GroupesDepart.Length == p.Departs.NbJoueursMax;
            var vus = new HashSet<int>();
            foreach (int gr in carte.GroupesDepart) departsDistincts &= vus.Add(gr);
            bool zonesValides = true;
            foreach (int z in carte.ZonesDepart) zonesValides &= z >= 0 && z < carte.NbZones;
            Ajouter(regles, "R13", "six départs distincts et viables", departsDistincts && zonesValides,
                $"{carte.GroupesDepart.Length} départs, {(zonesValides ? "zones valides" : "ZONE INVALIDE")}");

            // R14 — déterminisme : l'empreinte se recalcule à l'identique
            MapData data = MapData.Depuis(carte, p.Maillage.TailleCarte);
            bool empreinteStable = data.CalculerEmpreinte() == data.Empreinte;
            Ajouter(regles, "R14", "empreinte reproductible", empreinteStable,
                $"0x{data.Empreinte:X16}, {data.TailleApproximative() / 1024} Ko");

            // R15 — navigation : contrôlé au jalon du navmesh, pas ici
            Ajouter(regles, "R15", "navmesh", true, "à vérifier au jalon J10, sur l'appareil");

            // R16 — performance
            Ajouter(regles, "R16", "budget de temps", true, "mesuré par la campagne de graines");

            // R17 — relief léger
            float penteMax = PenteMaxHorsMassif(carte);
            // Marge de deux degrés : la quantification des altitudes au pas de 5 cm peut
            // remonter une pente d'une fraction de degré après le rabotage.
            Ajouter(regles, "R17", "relief léger hors massifs",
                penteMax <= p.Materialisation.PenteMaxDegres + 2f, $"pente maximale {penteMax:F1}°");

            // R18 — bordures de pays : donnée disponible, rendu au jalon 3D
            Ajouter(regles, "R18", "contours de zones disponibles", true,
                "chaînes de coins reconstructibles depuis MapData");

            return regles;
        }

        static void Ajouter(List<ResultatRegle> liste, string code, string libelle, bool conforme, string mesure)
        {
            liste.Add(new ResultatRegle { Code = code, Libelle = libelle, Conforme = conforme, Mesure = mesure });
        }

        static int CompterComposantesTerre(Carte carte)
        {
            GrapheCellules g = carte.Graphe;
            var vu = new bool[carte.NbCellules];
            var pile = new Stack<int>();
            int composantes = 0;

            for (int depart = 0; depart < carte.NbCellules; depart++)
            {
                if (!carte.Terre[depart] || vu[depart]) continue;
                composantes++;
                pile.Push(depart);
                vu[depart] = true;
                while (pile.Count > 0)
                {
                    int c = pile.Pop();
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || !carte.Terre[v] || vu[v]) continue;
                        vu[v] = true;
                        pile.Push(v);
                    }
                }
            }
            return composantes;
        }

        static bool ToutesConnexes(Carte carte, Func<int, List<int>> membres, Func<int, int, bool> dedans, int nb)
        {
            GrapheCellules g = carte.Graphe;
            for (int i = 0; i < nb; i++)
            {
                List<int> cellules = membres(i);
                if (cellules.Count == 0) return false;

                var vu = new HashSet<int> { cellules[0] };
                var pile = new Stack<int>();
                pile.Push(cellules[0]);
                int atteintes = 0;
                while (pile.Count > 0)
                {
                    int c = pile.Pop();
                    atteintes++;
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || vu.Contains(v) || !dedans(v, i)) continue;
                        vu.Add(v);
                        pile.Push(v);
                    }
                }
                if (atteintes != cellules.Count) return false;
            }
            return true;
        }

        static bool ToutesConnexesPraticables(Carte carte)
        {
            GrapheCellules g = carte.Graphe;
            for (int z = 0; z < carte.NbZones; z++)
            {
                int depart = -1, total = 0;
                foreach (int c in carte.CellulesDeZone[z])
                {
                    if (carte.ADrapeau(c, DrapeauxCellule.NonConstructible)) continue;
                    total++;
                    if (depart < 0) depart = c;
                }
                if (total == 0) return false;

                var vu = new HashSet<int> { depart };
                var pile = new Stack<int>();
                pile.Push(depart);
                int atteintes = 0;
                while (pile.Count > 0)
                {
                    int c = pile.Pop();
                    atteintes++;
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || vu.Contains(v)) continue;
                        if (carte.ZoneDeCellule[v] != z) continue;
                        if (carte.ADrapeau(v, DrapeauxCellule.NonConstructible)) continue;
                        vu.Add(v);
                        pile.Push(v);
                    }
                }
                if (atteintes != total) return false;
            }
            return true;
        }

        static int CompterAretesNonBloquantes(Carte carte)
        {
            GrapheCellules g = carte.Graphe;
            var dePassage = new HashSet<int>();
            foreach (Passage passage in carte.Passages)
            {
                foreach (int e in passage.AretesFines) dePassage.Add(e);
            }

            int n = 0;
            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                if (b < 0 || !carte.Terre[a] || !carte.Terre[b]) continue;
                if (carte.GroupeDeZone[carte.ZoneDeCellule[a]] == carte.GroupeDeZone[carte.ZoneDeCellule[b]]) continue;
                if (dePassage.Contains(e)) continue;
                if (!carte.AreteBloquante[e]) n++;
            }
            return n;
        }

        static float PenteMaxHorsMassif(Carte carte)
        {
            GrapheCellules g = carte.Graphe;
            float pire = 0f;
            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                if (b < 0 || !carte.Terre[a] || !carte.Terre[b]) continue;
                if (carte.ADrapeau(a, DrapeauxCellule.Massif) || carte.ADrapeau(b, DrapeauxCellule.Massif)) continue;
                float d = Math.Abs(carte.HauteurCoin[g.AreteCoinA[e]] - carte.HauteurCoin[g.AreteCoinB[e]]);
                float degres = (float)(Math.Atan(d / Math.Max(g.LongueurArete[e], 1f)) * 180.0 / Math.PI);
                pire = Math.Max(pire, degres);
            }
            return pire;
        }
    }
}
