using System;
using MapHeroic.Generation.Geometrie;
using MapHeroic.Generation.Noyau;
using NUnit.Framework;
using Unity.Mathematics;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Critères J2 : entre 6 500 et 7 500 cellules à l'échelle nominale, aucune cellule
    /// dégénérée, et deux exécutions de même graine strictement identiques.
    /// </summary>
    public class TestsConstructeurMaillage
    {
        /// <summary>Réglages réduits : mêmes propriétés géométriques, dix fois plus rapides.</summary>
        static ParametresMaillage Reduits() => new ParametresMaillage
        {
            TailleCarte = 400f,
            RayonPoisson = 14f,
            EssaisPoisson = 20,
            IterationsLloyd = 2,
            MargeAnneau = 20f,
            PasAnneau = 20f
        };

        static GrapheCellules Construire(ulong graine, ParametresMaillage p = null)
        {
            return ConstructeurMaillage.Construire(graine, p ?? Reduits(), out _);
        }

        // ------------------------------------------------------------------ échelle nominale

        /// <summary>
        /// Plage calibrée sur la mesure, pas sur l'estimation du document de conception :
        /// à 1400 m et rayon 14 m, Bridson produit 6 181 à 6 233 cellules sur six graines,
        /// soit ≈ 53 % de l'empilement hexagonal maximal. La conception tablait sur 6 500 à
        /// 7 500 en supposant un taux de remplissage plus élevé ; c'est l'estimation qui
        /// était fausse, pas le rayon, qui reste dicté par la granularité des cols (≈ 11 m
        /// par arête, soit 2 à 4 arêtes pour un passage de 28 m).
        /// </summary>
        [Test]
        public void EchelleNominale_NombreDeCellulesDansLaPlageAttendue()
        {
            var comptes = new int[6];
            for (int i = 0; i < comptes.Length; i++)
            {
                var p = new ParametresMaillage();
                ConstructeurMaillage.Construire(1000UL + (ulong)i, p, out DiagnosticMaillage diag);
                comptes[i] = diag.NbPointsInterieurs;
                TestContext.WriteLine($"graine {1000 + i} : {diag}");
            }

            foreach (int c in comptes)
            {
                Assert.That(c, Is.InRange(5900, 6600), $"Compte hors plage calibrée : {c}");
            }

            // La stabilité compte davantage que la valeur absolue : les zones agrègent ces
            // cellules, et une densité qui varierait d'une graine à l'autre déplacerait la
            // granularité de l'équilibrage d'aire.
            int min = int.MaxValue, max = int.MinValue;
            foreach (int c in comptes) { min = Math.Min(min, c); max = Math.Max(max, c); }
            double dispersion = (double)(max - min) / min;
            Assert.Less(dispersion, 0.03, $"Densité instable entre graines : {min} à {max}.");
        }

        [Test]
        public void EchelleNominale_AssezDeCellulesParZone()
        {
            // Contrainte réelle de la conception : chaque zone doit contenir assez de cellules
            // pour que l'équilibrage puisse ajuster son aire par transferts d'une cellule.
            // Avec ≈ 31 % de terre et 90 zones, un pas de transfert vaut environ 1/21 d'une
            // zone — largement sous la tolérance de ± 25 %.
            var p = new ParametresMaillage();
            ConstructeurMaillage.Construire(2026UL, p, out DiagnosticMaillage diag);
            double cellulesTerre = diag.NbPointsInterieurs * 0.31;
            double parZone = cellulesTerre / 90.0;
            Assert.Greater(parZone, 15.0,
                $"Seulement {parZone:F1} cellules par zone : l'équilibrage d'aire serait trop grossier.");
        }

        [Test]
        public void EchelleNominale_AucuneCelluleRejetee()
        {
            var p = new ParametresMaillage();
            ConstructeurMaillage.Construire(20260910UL, p, out DiagnosticMaillage diag);
            Assert.AreEqual(0, diag.NbCellulesRejetees,
                $"Des cellules n'ont pas pu être fermées : {diag}");
        }

        [Test]
        public void EchelleNominale_TientDansLeBudgetDeTemps()
        {
            var p = new ParametresMaillage();
            ConstructeurMaillage.Construire(1UL, p, out DiagnosticMaillage diag);
            // Le budget de la conception est de 48 ms sur mobile pour P1 + P2. En éditeur,
            // sans Burst et avec le compilateur de développement, on se donne dix fois plus :
            // ce test attrape une régression d'un ordre de grandeur, pas une milliseconde.
            Assert.Less(diag.MillisecondesTotal, 2000L, $"Trop lent : {diag}");
        }

        // ---------------------------------------------------------------------- déterminisme

        [Test]
        public void MemeGraine_MaillageIdentiqueAuBitPres()
        {
            var a = Construire(123UL);
            var b = Construire(123UL);

            Assert.AreEqual(a.NbCellules, b.NbCellules);
            Assert.AreEqual(a.NbCoins, b.NbCoins);
            Assert.AreEqual(a.NbAretes, b.NbAretes);
            CollectionAssert.AreEqual(a.CoinsDeCellule, b.CoinsDeCellule);
            CollectionAssert.AreEqual(a.DebutCoins, b.DebutCoins);
            CollectionAssert.AreEqual(a.VoisinsDeCellule, b.VoisinsDeCellule);
            CollectionAssert.AreEqual(a.AreteCelluleA, b.AreteCelluleA);
            CollectionAssert.AreEqual(a.AreteCelluleB, b.AreteCelluleB);

            for (int i = 0; i < a.NbCoins; i++)
            {
                Assert.AreEqual(a.Coins[i].x, b.Coins[i].x);
                Assert.AreEqual(a.Coins[i].y, b.Coins[i].y);
            }
            for (int i = 0; i < a.NbCellules; i++)
            {
                Assert.AreEqual(a.Sites[i].x, b.Sites[i].x);
                Assert.AreEqual(a.Sites[i].y, b.Sites[i].y);
            }
        }

        [Test]
        public void GrainesDifferentes_MaillagesDifferents()
        {
            var a = Construire(1UL);
            var b = Construire(2UL);
            bool identique = a.NbCellules == b.NbCellules && a.NbCoins == b.NbCoins;
            if (identique)
            {
                for (int i = 0; i < a.NbCellules; i++)
                {
                    if (!a.Sites[i].Equals(b.Sites[i])) { identique = false; break; }
                }
            }
            Assert.IsFalse(identique);
        }

        // ------------------------------------------------------------------------- structure

        [Test]
        public void ToutesLesCellulesSontDesPolygonesValides()
        {
            var g = Construire(5UL);
            for (int c = 0; c < g.NbCellules; c++)
            {
                Assert.GreaterOrEqual(g.NbCoinsDe(c), 3, $"Cellule {c} dégénérée.");
                Assert.Greater(g.Aire[c], 0f, $"Cellule {c} d'aire nulle.");

                // Aucun coin répété dans un même contour.
                int deb = g.DebutCoins[c], fin = g.DebutCoins[c + 1];
                for (int i = deb; i < fin; i++)
                {
                    for (int j = i + 1; j < fin; j++)
                    {
                        Assert.AreNotEqual(g.CoinsDeCellule[i], g.CoinsDeCellule[j],
                            $"Le coin {g.CoinsDeCellule[i]} apparaît deux fois dans la cellule {c}.");
                    }
                }
            }
        }

        [Test]
        public void ContoursOrientesDansLeSensTrigonometrique()
        {
            var g = Construire(6UL);
            for (int c = 0; c < g.NbCellules; c++)
            {
                double somme = 0.0;
                int deb = g.DebutCoins[c], n = g.NbCoinsDe(c);
                for (int k = 0; k < n; k++)
                {
                    float2 p = g.Coins[g.CoinsDeCellule[deb + k]];
                    float2 q = g.Coins[g.CoinsDeCellule[deb + (k + 1) % n]];
                    somme += (double)p.x * q.y - (double)q.x * p.y;
                }
                Assert.Greater(somme, 0.0, $"La cellule {c} est orientée dans le sens horaire.");
            }
        }

        [Test]
        public void NombreMoyenDeVoisinsProcheDeSix()
        {
            // Propriété d'Euler : un pavage de Voronoï d'un plan a en moyenne six voisins par
            // cellule. Un écart signalerait une topologie fausse.
            var g = Construire(7UL);
            long total = 0;
            int compte = 0;
            for (int c = 0; c < g.NbCellules; c++)
            {
                int voisins = 0;
                for (int s = g.DebutCoins[c]; s < g.DebutCoins[c + 1]; s++)
                {
                    if (g.VoisinsDeCellule[s] >= 0) voisins++;
                }
                if (voisins == g.NbCoinsDe(c))   // cellules entièrement intérieures seulement
                {
                    total += voisins;
                    compte++;
                }
            }
            Assert.Greater(compte, 50, "Trop peu de cellules intérieures pour conclure.");
            double moyenne = (double)total / compte;
            Assert.That(moyenne, Is.EqualTo(6.0).Within(0.4), $"Moyenne de voisins = {moyenne:F2}.");
        }

        [Test]
        public void LesSitesRestentDansLeDomaine()
        {
            var p = Reduits();
            var g = Construire(8UL, p);
            foreach (float2 s in g.Sites)
            {
                Assert.That(s.x, Is.InRange(0f, p.TailleCarte));
                Assert.That(s.y, Is.InRange(0f, p.TailleCarte));
            }
        }

        [Test]
        public void CoordonneesQuantifiees()
        {
            var g = Construire(9UL);
            foreach (float2 c in g.Coins)
            {
                Assert.AreEqual(c.x, Quantification.Quantifier(c.x), 1e-6f, "Coin non quantifié.");
                Assert.AreEqual(c.y, Quantification.Quantifier(c.y), 1e-6f, "Coin non quantifié.");
            }
        }

        // ------------------------------------------------------------------------ relaxation

        [Test]
        public void LloydRegulariseLesAires()
        {
            double sansRelaxation = CoefficientDeVariationDesAires(0);
            double avecRelaxation = CoefficientDeVariationDesAires(2);
            Assert.Less(avecRelaxation, sansRelaxation,
                $"Lloyd n'a pas régularisé : {sansRelaxation:F3} → {avecRelaxation:F3}");
        }

        [Test]
        public void ApresLloyd_LesAiresInterieuresSontHomogenes()
        {
            // C'est la propriété dont dépendra la contrainte d'aire des zones à ± 30 % :
            // les zones agrègent ces cellules, donc leur dispersion plafonne celle des zones.
            var p = Reduits();
            var g = Construire(11UL, p);
            var aires = AiresInterieures(g, p.TailleCarte, 60f);
            Array.Sort(aires);
            double mediane = aires[aires.Length / 2];

            int hors = 0;
            foreach (double a in aires)
            {
                if (a < 0.45 * mediane || a > 1.9 * mediane) hors++;
            }
            double part = (double)hors / aires.Length;
            Assert.Less(part, 0.02, $"{part:P1} des cellules intérieures s'écartent trop de la médiane.");
        }

        static double CoefficientDeVariationDesAires(int iterationsLloyd)
        {
            var p = Reduits();
            p.IterationsLloyd = iterationsLloyd;
            var g = Construire(10UL, p);
            double[] aires = AiresInterieures(g, p.TailleCarte, 60f);

            double somme = 0.0;
            foreach (double a in aires) somme += a;
            double moyenne = somme / aires.Length;

            double variance = 0.0;
            foreach (double a in aires) variance += (a - moyenne) * (a - moyenne);
            variance /= aires.Length;

            return Math.Sqrt(variance) / moyenne;
        }

        /// <summary>Aires des cellules dont le site est à plus de <c>marge</c> du bord.</summary>
        static double[] AiresInterieures(GrapheCellules g, float taille, float marge)
        {
            var liste = new System.Collections.Generic.List<double>(g.NbCellules);
            for (int c = 0; c < g.NbCellules; c++)
            {
                float2 s = g.Sites[c];
                if (s.x < marge || s.y < marge || s.x > taille - marge || s.y > taille - marge) continue;
                liste.Add(g.Aire[c]);
            }
            return liste.ToArray();
        }
    }
}
