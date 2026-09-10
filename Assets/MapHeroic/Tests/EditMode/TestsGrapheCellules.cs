using System;
using MapHeroic.Generation.Noyau;
using NUnit.Framework;
using Unity.Mathematics;

namespace MapHeroic.Tests
{
    /// <summary>
    /// La reconstruction des tables dérivées est le contrat sur lequel repose la
    /// sérialisation de MapData : seuls les coins de cellules sont enregistrés, tout le reste
    /// doit se retrouver à l'identique. On la vérifie sur une grille carrée, dont on connaît
    /// toutes les réponses à l'avance.
    /// </summary>
    public class TestsGrapheCellules
    {
        /// <summary>Grille de cotes × cotes cellules unitaires, coins partagés, sens trigonométrique.</summary>
        static GrapheCellules GrilleCarree(int cotes)
        {
            int parCote = cotes + 1;
            var g = new GrapheCellules
            {
                NbCellules = cotes * cotes,
                NbCoins = parCote * parCote
            };

            g.Coins = new float2[g.NbCoins];
            for (int gy = 0; gy < parCote; gy++)
            {
                for (int gx = 0; gx < parCote; gx++)
                {
                    g.Coins[gy * parCote + gx] = new float2(gx, gy);
                }
            }

            g.Sites = new float2[g.NbCellules];
            g.DebutCoins = new int[g.NbCellules + 1];
            g.CoinsDeCellule = new int[g.NbCellules * 4];

            for (int cy = 0; cy < cotes; cy++)
            {
                for (int cx = 0; cx < cotes; cx++)
                {
                    int c = cy * cotes + cx;
                    int b = c * 4;
                    g.Sites[c] = new float2(cx + 0.5f, cy + 0.5f);
                    g.DebutCoins[c] = b;
                    g.CoinsDeCellule[b + 0] = cy * parCote + cx;
                    g.CoinsDeCellule[b + 1] = cy * parCote + cx + 1;
                    g.CoinsDeCellule[b + 2] = (cy + 1) * parCote + cx + 1;
                    g.CoinsDeCellule[b + 3] = (cy + 1) * parCote + cx;
                }
            }
            g.DebutCoins[g.NbCellules] = g.NbCellules * 4;
            return g;
        }

        [Test]
        public void Grille3x3_NombreDAretesAttendu()
        {
            var g = GrilleCarree(3);
            g.ReconstruireDerives();
            // 4 rangées × 3 segments horizontaux + 4 colonnes × 3 segments verticaux.
            Assert.AreEqual(24, g.NbAretes);
        }

        [Test]
        public void Grille3x3_AiresUnitaires()
        {
            var g = GrilleCarree(3);
            g.ReconstruireDerives();
            for (int c = 0; c < g.NbCellules; c++)
            {
                Assert.AreEqual(1f, g.Aire[c], 1e-5f, $"Aire incorrecte pour la cellule {c}.");
            }
        }

        [Test]
        public void Grille3x3_LongueursUnitaires()
        {
            var g = GrilleCarree(3);
            g.ReconstruireDerives();
            for (int e = 0; e < g.NbAretes; e++)
            {
                Assert.AreEqual(1f, g.LongueurArete[e], 1e-5f, $"Longueur incorrecte pour l'arête {e}.");
            }
        }

        [Test]
        public void Grille3x3_VoisinagesAttendus()
        {
            var g = GrilleCarree(3);
            g.ReconstruireDerives();

            Assert.AreEqual(2, NbVoisinsReels(g, 0), "Cellule de coin.");
            Assert.AreEqual(3, NbVoisinsReels(g, 1), "Cellule de bord.");
            Assert.AreEqual(4, NbVoisinsReels(g, 4), "Cellule centrale.");
            Assert.AreEqual(2, NbVoisinsReels(g, 8), "Cellule de coin opposé.");
        }

        [Test]
        public void Grille3x3_DouzeAretesDeBord()
        {
            var g = GrilleCarree(3);
            g.ReconstruireDerives();

            int creneauxDeBord = 0;
            for (int s = 0; s < g.NbCreneaux; s++)
            {
                if (g.VoisinsDeCellule[s] < 0) creneauxDeBord++;
            }
            Assert.AreEqual(12, creneauxDeBord, "Le périmètre d'une grille 3×3 compte 12 arêtes.");

            int aretesDeBord = 0;
            for (int e = 0; e < g.NbAretes; e++)
            {
                if (g.AreteCelluleB[e] < 0) aretesDeBord++;
            }
            Assert.AreEqual(12, aretesDeBord);
        }

        [Test]
        public void AreteEntre_TrouveLesVoisinesEtRejetteLesAutres()
        {
            var g = GrilleCarree(3);
            g.ReconstruireDerives();

            int e = g.AreteEntre(0, 1);
            Assert.GreaterOrEqual(e, 0, "Les cellules 0 et 1 sont voisines.");
            Assert.AreEqual(e, g.AreteEntre(1, 0), "La relation doit être symétrique.");
            Assert.AreEqual(0, g.AreteCelluleA[e]);
            Assert.AreEqual(1, g.AreteCelluleB[e]);

            Assert.AreEqual(-1, g.AreteEntre(0, 2), "Les cellules 0 et 2 ne se touchent pas.");
            Assert.AreEqual(-1, g.AreteEntre(0, 8), "Les coins opposés ne se touchent pas.");
        }

        [Test]
        public void CelluleA_EstToujoursLaPlusPetite()
        {
            var g = GrilleCarree(4);
            g.ReconstruireDerives();
            for (int e = 0; e < g.NbAretes; e++)
            {
                if (g.AreteCelluleB[e] >= 0)
                {
                    Assert.Less(g.AreteCelluleA[e], g.AreteCelluleB[e]);
                }
            }
        }

        [Test]
        public void GrapheDesCoins_EstCompletEtTrie()
        {
            var g = GrilleCarree(3);
            g.ReconstruireDerives();

            Assert.AreEqual(2 * g.NbAretes, g.DebutCoinsVoisins[g.NbCoins],
                "Chaque arête doit apparaître une fois dans chacun de ses deux coins.");

            for (int i = 0; i < g.NbCoins; i++)
            {
                int deb = g.DebutCoinsVoisins[i];
                int fin = g.DebutCoinsVoisins[i + 1];
                for (int k = deb + 1; k < fin; k++)
                {
                    Assert.Less(g.CoinsVoisins[k - 1], g.CoinsVoisins[k],
                        $"Les voisins du coin {i} doivent être triés et sans doublon.");
                }
            }

            // Le coin (0, 0) n'a que deux voisins ; le coin central en a quatre.
            Assert.AreEqual(2, g.DebutCoinsVoisins[1] - g.DebutCoinsVoisins[0]);
            int coinCentral = 1 * 4 + 1;
            Assert.AreEqual(4, g.DebutCoinsVoisins[coinCentral + 1] - g.DebutCoinsVoisins[coinCentral]);
        }

        [Test]
        public void Reconstruction_EstReproductibleAuBitPres()
        {
            var g = GrilleCarree(5);
            g.ReconstruireDerives();

            var aretes = (int[])g.AretesDeCellule.Clone();
            var voisins = (int[])g.VoisinsDeCellule.Clone();
            var celluleA = (int[])g.AreteCelluleA.Clone();
            var celluleB = (int[])g.AreteCelluleB.Clone();
            var coinsVoisins = (int[])g.CoinsVoisins.Clone();

            g.ReconstruireDerives();

            CollectionAssert.AreEqual(aretes, g.AretesDeCellule);
            CollectionAssert.AreEqual(voisins, g.VoisinsDeCellule);
            CollectionAssert.AreEqual(celluleA, g.AreteCelluleA);
            CollectionAssert.AreEqual(celluleB, g.AreteCelluleB);
            CollectionAssert.AreEqual(coinsVoisins, g.CoinsVoisins);
        }

        [Test]
        public void ChaqueCreneau_PointeVersUneAreteCoherente()
        {
            var g = GrilleCarree(4);
            g.ReconstruireDerives();

            for (int c = 0; c < g.NbCellules; c++)
            {
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int e = g.AretesDeCellule[s];
                    bool appartient = g.AreteCelluleA[e] == c || g.AreteCelluleB[e] == c;
                    Assert.IsTrue(appartient, $"L'arête {e} du créneau {s} n'appartient pas à la cellule {c}.");

                    int voisin = g.VoisinsDeCellule[s];
                    int attendu = g.AreteCelluleA[e] == c ? g.AreteCelluleB[e] : g.AreteCelluleA[e];
                    Assert.AreEqual(attendu, voisin, $"Voisin incohérent au créneau {s}.");
                }
            }
        }

        [Test]
        public void MaillageInvalide_EstRejete()
        {
            var trop = GrilleCarree(1);
            trop.CoinsDeCellule = new[] { 0, 1 };
            trop.DebutCoins = new[] { 0, 2 };
            Assert.Throws<InvalidOperationException>(() => trop.ReconstruireDerives(),
                "Un polygone de moins de trois coins doit être refusé.");

            var horsPlage = GrilleCarree(1);
            horsPlage.CoinsDeCellule[0] = 999;
            Assert.Throws<InvalidOperationException>(() => horsPlage.ReconstruireDerives(),
                "Un index de coin hors plage doit être refusé.");

            // Trois cellules superposées : chaque arête serait portée trois fois.
            var triple = new GrapheCellules
            {
                NbCellules = 3,
                NbCoins = 4,
                Coins = new[] { new float2(0, 0), new float2(1, 0), new float2(1, 1), new float2(0, 1) },
                Sites = new[] { new float2(0.5f, 0.5f), new float2(0.5f, 0.5f), new float2(0.5f, 0.5f) },
                CoinsDeCellule = new[] { 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3 },
                DebutCoins = new[] { 0, 4, 8, 12 }
            };
            Assert.Throws<InvalidOperationException>(() => triple.ReconstruireDerives(),
                "Une arête portée par plus de deux cellules doit être refusée.");
        }

        static int NbVoisinsReels(GrapheCellules g, int cellule)
        {
            int n = 0;
            for (int s = g.DebutCoins[cellule]; s < g.DebutCoins[cellule + 1]; s++)
            {
                if (g.VoisinsDeCellule[s] >= 0) n++;
            }
            return n;
        }
    }
}
