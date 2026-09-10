using System;
using MapHeroic.Generation;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;
using NUnit.Framework;
using Unity.Mathematics;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Critères J3 : île unique dans la fenêtre de ratio attendue, aucune terre contre le
    /// bord du domaine, tout coin possédant un chemin descendant vers la mer, 4 à 6 rivières,
    /// crêtes sur 20 à 30 % des terres, pente moyenne douce.
    /// </summary>
    public class TestsIleEtRelief
    {
        const int NbGrainesCampagne = 40;

        /// <summary>
        /// La génération s'arrête à l'hydrologie : au-delà, la matérialisation creuse les
        /// lits et surélève les massifs, si bien que les altitudes ne sont plus celles que
        /// cette phase a produites. Vérifier ses invariants sur la carte finale reviendrait à
        /// lui reprocher le travail des phases suivantes.
        /// </summary>
        static ParametresGeneration JusquALHydrologie()
        {
            return new ParametresGeneration { PhaseFinale = PhaseGeneration.Hydrologie };
        }

        static Carte Generer(ulong graine, out RapportGeneration rapport)
        {
            return GenerateurCarte.Generer(graine, JusquALHydrologie(), out rapport);
        }

        static Carte GenererValide(ulong graine)
        {
            var carte = Generer(graine, out RapportGeneration rapport);
            Assert.IsNotNull(carte, $"Génération échouée pour la graine {graine} : {rapport}");
            return carte;
        }

        // ------------------------------------------------------------------- campagne

        /// <summary>
        /// On juge la phase P3 sur son propre diagnostic, pas sur la réussite du pipeline
        /// entier : une carte rejetée plus tard par les groupes ou les passages n'est pas un
        /// échec de l'île. Le taux de réussite de bout en bout est mesuré à part, dans
        /// <see cref="TestsPipeline"/>.
        /// </summary>
        [Test]
        public void Campagne_IleAcceptableEtPeuDeRejeux()
        {
            int total = 0, echecs = 0, rejeux = 0;
            float ratioMin = 1f, ratioMax = 0f;

            for (int i = 0; i < NbGrainesCampagne; i++)
            {
                Generer(5000UL + (ulong)i, out RapportGeneration rapport);
                total++;
                if (rapport.Ile == null || !rapport.Ile.Reussi) { echecs++; continue; }

                rejeux += rapport.Ile.Essais - 1;
                ratioMin = math.min(ratioMin, rapport.Ile.RatioTerre);
                ratioMax = math.max(ratioMax, rapport.Ile.RatioTerre);
            }

            TestContext.WriteLine($"{total} graines : {echecs} échecs, {rejeux} rejeux, " +
                                  $"ratio de terre {ratioMin:P1} à {ratioMax:P1}");

            Assert.AreEqual(0, echecs, "Des graines n'ont produit aucune île acceptable.");
            Assert.LessOrEqual(rejeux, total * 3 / 100 + 2,
                $"{rejeux} rejeux sur {total} graines : trop de tirages sont jetés.");
            Assert.That(ratioMin, Is.GreaterThanOrEqualTo(0.26f));
            Assert.That(ratioMax, Is.LessThanOrEqualTo(0.38f));
        }

        // ------------------------------------------------------------------ P3 : île

        [Test]
        public void UneSeuleComposanteTerrestre()
        {
            var carte = GenererValide(11UL);
            GrapheCellules g = carte.Graphe;

            var vu = new bool[g.NbCellules];
            var file = new int[g.NbCellules];
            int composantes = 0;

            for (int depart = 0; depart < g.NbCellules; depart++)
            {
                if (!carte.Terre[depart] || vu[depart]) continue;
                composantes++;
                int tete = 0, queue = 0;
                file[queue++] = depart;
                vu[depart] = true;
                while (tete < queue)
                {
                    int c = file[tete++];
                    for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                    {
                        int v = g.VoisinsDeCellule[s];
                        if (v < 0 || !carte.Terre[v] || vu[v]) continue;
                        vu[v] = true;
                        file[queue++] = v;
                    }
                }
            }
            Assert.AreEqual(1, composantes, "L'île doit être d'un seul tenant.");
        }

        [Test]
        public void AucuneTerreContreLeBordDuDomaine()
        {
            var p = JusquALHydrologie();
            var carte = GenerateurCarte.Generer(12UL, p, out _);
            Assert.IsNotNull(carte);

            float marge = p.Ile.MargeBord;
            float limite = p.Maillage.TailleCarte - marge;

            for (int c = 0; c < carte.NbCellules; c++)
            {
                if (!carte.Terre[c]) continue;
                float2 s = carte.Graphe.Sites[c];
                Assert.IsTrue(s.x >= marge && s.y >= marge && s.x <= limite && s.y <= limite,
                    $"La cellule de terre {c} est à ({s.x:F0}, {s.y:F0}), dans la marge de bord.");
            }
        }

        [Test]
        public void AucuneCelluleTerreNeToucheLeBordDuMaillage()
        {
            var carte = GenererValide(13UL);
            GrapheCellules g = carte.Graphe;
            for (int c = 0; c < g.NbCellules; c++)
            {
                if (!carte.Terre[c]) continue;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    Assert.GreaterOrEqual(g.VoisinsDeCellule[s], 0,
                        $"La cellule de terre {c} borde le vide du maillage.");
                }
            }
        }

        [Test]
        public void DistanceCoteCoherente()
        {
            var carte = GenererValide(14UL);
            GrapheCellules g = carte.Graphe;

            for (int c = 0; c < g.NbCellules; c++)
            {
                bool voisineDeMer = false, voisineDeTerre = false;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    int v = g.VoisinsDeCellule[s];
                    if (v < 0 || !carte.Terre[v]) voisineDeMer = true;
                    else voisineDeTerre = true;
                }

                if (carte.Terre[c])
                {
                    Assert.GreaterOrEqual(carte.DistCote[c], 0, $"Cellule de terre {c} à distance négative.");
                    if (voisineDeMer) Assert.AreEqual(0, carte.DistCote[c], $"La cellule {c} est une plage.");
                    else Assert.Greater(carte.DistCote[c], 0);
                }
                else
                {
                    Assert.Less(carte.DistCote[c], 0, $"Cellule de mer {c} à distance positive.");
                    if (voisineDeTerre) Assert.AreEqual(-1, carte.DistCote[c]);
                }
            }
        }

        // -------------------------------------------------------------- P4 : relief

        [Test]
        public void CretesSurLaPartVisee()
        {
            var p = JusquALHydrologie();
            GenerateurCarte.Generer(15UL, p, out RapportGeneration rapport);
            Assert.That(rapport.Relief.PartCellulesEnCrete, Is.InRange(0.20f, 0.30f),
                $"Couverture des crêtes hors cible : {rapport.Relief}");
        }

        [Test]
        public void ReliefLeger()
        {
            for (ulong graine = 16UL; graine < 20UL; graine++)
            {
                var carte = GenerateurCarte.Generer(graine, JusquALHydrologie(), out RapportGeneration rapport);
                Assert.IsNotNull(carte);
                TestContext.WriteLine($"graine {graine} : {rapport.Relief}");

                Assert.Less(rapport.Relief.PenteMoyenneDegres, 4f,
                    $"Le relief n'est pas assez doux : {rapport.Relief}");
                Assert.That(rapport.Relief.HauteurMedianeTerre, Is.InRange(2f, 20f),
                    $"Altitude médiane inattendue : {rapport.Relief}");
            }
        }

        [Test]
        public void LaTerreEstAuDessusDuNiveauDeLaMer()
        {
            var carte = GenererValide(17UL);
            GrapheCellules g = carte.Graphe;

            for (int coin = 0; coin < g.NbCoins; coin++)
            {
                bool touchePlusieursTerres = true;
                int deb = g.DebutCellulesDeCoin[coin], fin = g.DebutCellulesDeCoin[coin + 1];
                if (deb == fin) continue;
                for (int i = deb; i < fin; i++)
                {
                    if (!carte.Terre[g.CellulesDeCoin[i]]) { touchePlusieursTerres = false; break; }
                }
                if (touchePlusieursTerres)
                {
                    Assert.Greater(carte.HauteurCoin[coin], 0f,
                        $"Le coin {coin}, entouré de terre, est sous le niveau de la mer.");
                }
            }
        }

        // ---------------------------------------------------------- P4 : hydrologie

        [Test]
        public void ToutCoinPossedeUnCheminDescendantVersLaMer()
        {
            var carte = GenererValide(18UL);
            GenerateurCarte.Generer(18UL, JusquALHydrologie(), out RapportGeneration rapport);

            Assert.AreEqual(0, rapport.Hydrologie.NbCoinsSansEcoulement,
                "Des coins n'ont aucun voisin plus bas : le comblement des dépressions a échoué.");

            // Vérification directe : suivre l'aval depuis chaque coin doit aboutir.
            for (int depart = 0; depart < carte.NbCoins; depart += 7)
            {
                int c = depart;
                int pas = 0;
                while (carte.Aval[c] >= 0)
                {
                    float avant = carte.HauteurCoin[c];
                    c = carte.Aval[c];
                    Assert.Less(carte.HauteurCoin[c], avant, "L'écoulement doit toujours descendre.");
                    if (++pas > 5000) Assert.Fail($"Écoulement sans fin depuis le coin {depart}.");
                }
            }
        }

        [Test]
        public void NombreDeRivieresDansLIntervalle()
        {
            for (ulong graine = 20UL; graine < 26UL; graine++)
            {
                GenerateurCarte.Generer(graine, JusquALHydrologie(), out RapportGeneration rapport);
                Assert.That(rapport.Hydrologie.NbRivieres, Is.InRange(4, 6),
                    $"Graine {graine} : {rapport.Hydrologie}");
            }
        }

        [Test]
        public void LesRivieresDescendentEtSeTerminentALaMer()
        {
            var carte = GenererValide(27UL);
            Assert.Greater(carte.Rivieres.Count, 0);

            foreach (int[] riviere in carte.Rivieres)
            {
                Assert.GreaterOrEqual(riviere.Length, 3);
                for (int i = 1; i < riviere.Length; i++)
                {
                    Assert.LessOrEqual(carte.HauteurCoin[riviere[i]], carte.HauteurCoin[riviere[i - 1]],
                        "Une rivière doit descendre de la source vers l'embouchure.");
                }

                int embouchure = riviere[riviere.Length - 1];
                bool toucheLaMer = false;
                GrapheCellules g = carte.Graphe;
                for (int i = g.DebutCellulesDeCoin[embouchure]; i < g.DebutCellulesDeCoin[embouchure + 1]; i++)
                {
                    if (!carte.Terre[g.CellulesDeCoin[i]]) { toucheLaMer = true; break; }
                }
                Assert.IsTrue(toucheLaMer, "Une rivière doit finir à la mer.");
            }
        }

        [Test]
        public void DureteBorneeEtMaximaleAuBordDeMer()
        {
            var carte = GenererValide(28UL);
            GrapheCellules g = carte.Graphe;

            for (int e = 0; e < g.NbAretes; e++)
            {
                Assert.That(carte.Durete[e], Is.InRange(0f, 1f), $"Dureté hors bornes sur l'arête {e}.");

                int a = g.AreteCelluleA[e], b = g.AreteCelluleB[e];
                bool bordDeMer = b < 0 || !carte.Terre[a] || !carte.Terre[b];
                if (bordDeMer) Assert.AreEqual(1f, carte.Durete[e], "La mer doit être infranchissable.");
            }
        }

        // ------------------------------------------------------------- déterminisme

        [Test]
        public void MemeGraine_MemeCarte()
        {
            var a = GenererValide(99UL);
            var b = GenererValide(99UL);

            CollectionAssert.AreEqual(a.Terre, b.Terre);
            CollectionAssert.AreEqual(a.DistCote, b.DistCote);
            CollectionAssert.AreEqual(a.Aval, b.Aval);
            CollectionAssert.AreEqual(a.CoinRiviere, b.CoinRiviere);
            Assert.AreEqual(a.Rivieres.Count, b.Rivieres.Count);

            for (int i = 0; i < a.NbCoins; i++)
            {
                Assert.AreEqual(a.HauteurCoin[i], b.HauteurCoin[i], $"Altitude différente au coin {i}.");
                Assert.AreEqual(a.Flux[i], b.Flux[i], $"Flux différent au coin {i}.");
            }
            for (int e = 0; e < a.NbAretes; e++)
            {
                Assert.AreEqual(a.Durete[e], b.Durete[e], $"Dureté différente sur l'arête {e}.");
            }
        }

        [Test]
        public void EmpreinteStableEntreExecutions()
        {
            ulong Empreinte(Carte c)
            {
                var h = HashFnv.Nouveau();
                for (int i = 0; i < c.NbCellules; i++) { h.Booleen(c.Terre[i]); h.Entier(c.DistCote[i]); }
                for (int i = 0; i < c.NbCoins; i++) h.Entier(c.Aval[i]);
                return h.Valeur;
            }

            Assert.AreEqual(Empreinte(GenererValide(77UL)), Empreinte(GenererValide(77UL)));
            Assert.AreNotEqual(Empreinte(GenererValide(77UL)), Empreinte(GenererValide(78UL)));
        }

        // ------------------------------------------------------------------ mesures

        [Test]
        public void BudgetDeTempsRespecte()
        {
            GenerateurCarte.Generer(31UL, JusquALHydrologie(), out RapportGeneration rapport);
            TestContext.WriteLine(rapport.ToString());
            Assert.Less(rapport.MillisecondesTotal, 2000L, $"Trop lent : {rapport}");
        }
    }
}
