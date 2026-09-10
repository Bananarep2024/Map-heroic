using System.Collections.Generic;
using MapHeroic.Generation;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;
using NUnit.Framework;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Critères J4 : exactement 90 zones connexes, toutes dans ± 30 % de la médiane et la
    /// quasi-totalité dans ± 25 %, aucune zone d'articulation isolant moins de trois zones.
    /// </summary>
    public class TestsZones
    {
        const int NbGrainesCampagne = 25;

        static Carte Generer(ulong graine, out RapportGeneration rapport)
        {
            return GenerateurCarte.Generer(graine, new ParametresGeneration(), out rapport);
        }

        static Carte GenererValide(ulong graine)
        {
            var carte = Generer(graine, out RapportGeneration rapport);
            Assert.IsNotNull(carte, $"Génération échouée pour la graine {graine} : {rapport}");
            return carte;
        }

        [Test]
        public void Campagne_QuatreVingtDixZonesEquilibrees()
        {
            int echecs = 0, horsInterne = 0, totalZones = 0;
            float pireEcart = 0f;
            long msMax = 0;

            for (int i = 0; i < NbGrainesCampagne; i++)
            {
                var carte = Generer(7000UL + (ulong)i, out RapportGeneration rapport);
                if (carte == null)
                {
                    echecs++;
                    TestContext.WriteLine($"graine {7000 + i} : ÉCHEC — {rapport.MotifEchec}");
                    continue;
                }

                Assert.AreEqual(90, carte.NbZones);
                horsInterne += rapport.Zones.NbHorsToleranceInterne;
                totalZones += 90;
                if (rapport.Zones.EcartMax > pireEcart) pireEcart = rapport.Zones.EcartMax;
                if (rapport.Zones.Millisecondes > msMax) msMax = rapport.Zones.Millisecondes;
            }

            float partDansTolerance = 1f - (float)horsInterne / totalZones;
            TestContext.WriteLine($"{NbGrainesCampagne} graines : {echecs} échecs, écart max {pireEcart:P1}, " +
                                  $"{partDansTolerance:P1} des zones dans ± 25 %, P5 au plus {msMax} ms");

            Assert.AreEqual(0, echecs, "Des graines n'ont produit aucun découpage acceptable.");
            Assert.LessOrEqual(pireEcart, 0.30f, "Une zone sort de la tolérance de ± 30 %.");
            Assert.GreaterOrEqual(partDansTolerance, 0.95f,
                "Moins de 95 % des zones tiennent dans la cible interne de ± 25 %.");
        }

        [Test]
        public void ExactementQuatreVingtDixZonesNonVides()
        {
            var carte = GenererValide(41UL);
            Assert.AreEqual(90, carte.NbZones);
            for (int z = 0; z < 90; z++)
            {
                Assert.Greater(carte.CellulesDeZone[z].Count, 0, $"La zone {z} est vide.");
            }
        }

        [Test]
        public void ToutesLesCellulesDeTerreSontAffecteesEtAucuneCelluleDeMer()
        {
            var carte = GenererValide(42UL);
            for (int c = 0; c < carte.NbCellules; c++)
            {
                if (carte.Terre[c])
                {
                    Assert.GreaterOrEqual(carte.ZoneDeCellule[c], 0, $"La cellule de terre {c} n'a pas de zone.");
                    Assert.Less(carte.ZoneDeCellule[c], 90);
                }
                else
                {
                    Assert.AreEqual(-1, carte.ZoneDeCellule[c], $"La cellule de mer {c} a été affectée à une zone.");
                }
            }

            int total = 0;
            for (int z = 0; z < 90; z++) total += carte.CellulesDeZone[z].Count;
            Assert.AreEqual(carte.NbCellulesTerre, total, "Le découpage ne couvre pas exactement les terres.");
        }

        [Test]
        public void ChaqueZoneEstDUnSeulTenant()
        {
            var carte = GenererValide(43UL);
            GrapheCellules g = carte.Graphe;

            for (int z = 0; z < 90; z++)
            {
                List<int> cellules = carte.CellulesDeZone[z];
                var vu = new HashSet<int> { cellules[0] };
                var pile = new Stack<int>();
                pile.Push(cellules[0]);

                while (pile.Count > 0)
                {
                    int c = pile.Pop();
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || carte.ZoneDeCellule[v] != z || vu.Contains(v)) continue;
                        vu.Add(v);
                        pile.Push(v);
                    }
                }
                Assert.AreEqual(cellules.Count, vu.Count, $"La zone {z} est en plusieurs morceaux.");
            }
        }

        [Test]
        public void LeGrapheDesZonesEstConnexeEtSansZoneIsolee()
        {
            var carte = GenererValide(44UL);

            for (int z = 0; z < 90; z++)
            {
                Assert.Greater(carte.ZonesVoisines[z].Count, 0, $"La zone {z} n'a aucune voisine.");
            }

            var vu = new bool[90];
            var pile = new Stack<int>();
            pile.Push(0);
            vu[0] = true;
            int atteintes = 1;
            while (pile.Count > 0)
            {
                int z = pile.Pop();
                foreach (int v in carte.ZonesVoisines[z])
                {
                    if (vu[v]) continue;
                    vu[v] = true;
                    atteintes++;
                    pile.Push(v);
                }
            }
            Assert.AreEqual(90, atteintes, "Le graphe des zones doit être connexe.");
        }

        [Test]
        public void LaRelationDeVoisinageEstSymetrique()
        {
            var carte = GenererValide(45UL);
            for (int z = 0; z < 90; z++)
            {
                foreach (int v in carte.ZonesVoisines[z])
                {
                    Assert.Contains(z, carte.ZonesVoisines[v], $"Les zones {z} et {v} ne se voient pas mutuellement.");
                }
            }
        }

        [Test]
        public void AucuneArticulationIsolantMoinsDeTroisZones()
        {
            for (ulong graine = 50UL; graine < 56UL; graine++)
            {
                GenerateurCarte.Generer(graine, new ParametresGeneration(), out RapportGeneration rapport);
                Assert.AreEqual(0, rapport.Zones.NbArticulationsGenantes,
                    $"Graine {graine} : {rapport.Zones}");
            }
        }

        [Test]
        public void LesFrontieresEpousentLeRelief()
        {
            // Une frontière de zone doit tomber plus souvent sur du terrain dur qu'une arête
            // prise au hasard : c'est tout l'objet de la pondération de la croissance. Sans
            // elle, les deux moyennes seraient identiques.
            var carte = GenererValide(46UL);
            GrapheCellules g = carte.Graphe;

            double sommeFrontiere = 0.0, sommeInterieure = 0.0;
            int nbFrontiere = 0, nbInterieure = 0;

            for (int e = 0; e < g.NbAretes; e++)
            {
                int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                if (b < 0 || !carte.Terre[a] || !carte.Terre[b]) continue;

                if (carte.ZoneDeCellule[a] != carte.ZoneDeCellule[b])
                {
                    sommeFrontiere += carte.Durete[e];
                    nbFrontiere++;
                }
                else
                {
                    sommeInterieure += carte.Durete[e];
                    nbInterieure++;
                }
            }

            double dureteFrontiere = sommeFrontiere / nbFrontiere;
            double dureteInterieure = sommeInterieure / nbInterieure;
            TestContext.WriteLine($"dureté moyenne : frontières {dureteFrontiere:F3}, intérieur {dureteInterieure:F3}");

            Assert.Greater(dureteFrontiere, dureteInterieure * 1.15,
                "Les frontières de zones ne suivent pas le relief davantage que le hasard.");
        }

        [Test]
        public void MemeGraine_MemeDecoupage()
        {
            var a = GenererValide(88UL);
            var b = GenererValide(88UL);
            CollectionAssert.AreEqual(a.ZoneDeCellule, b.ZoneDeCellule);

            var ha = HashFnv.De(a.ZoneDeCellule);
            var hb = HashFnv.De(b.ZoneDeCellule);
            Assert.AreEqual(ha, hb);
            Assert.AreNotEqual(ha, HashFnv.De(GenererValide(89UL).ZoneDeCellule));
        }

        [Test]
        public void BudgetDeTempsRespecte()
        {
            GenerateurCarte.Generer(61UL, new ParametresGeneration(), out RapportGeneration rapport);
            TestContext.WriteLine(rapport.ToString());
            Assert.Less(rapport.Zones.Millisecondes, 500L, $"Découpage trop lent : {rapport.Zones}");
        }
    }
}
