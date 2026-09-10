using System;
using MapHeroic.Generation.Noyau;
using NUnit.Framework;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Les tables remplacent la bibliothèque mathématique du système dans le générateur.
    /// On vérifie ici qu'elles en sont assez proches pour tous les usages du pipeline, et
    /// surtout qu'aucune entrée — y compris NaN et l'infini — ne peut produire de NaN.
    /// </summary>
    public class TestsTables
    {
        // Sur plusieurs tours, la réduction de période dominée par la précision du float
        // pèse plus lourd que la table elle-même : d'où une tolérance un cran au-dessus.
        const double ToleranceSin = 5e-5;
        const double ToleranceExp = 3e-4;
        const double ToleranceAtan = 1e-5;

        [Test]
        public void Sin_SuitLaReferenceSurPlusieursTours()
        {
            for (int i = -2000; i <= 2000; i++)
            {
                float angle = i * 0.01f;
                double attendu = Math.Sin(angle);
                Assert.That(Tables.Sin(angle), Is.EqualTo(attendu).Within(ToleranceSin),
                    $"Sin s'écarte de la référence en {angle}.");
            }
        }

        [Test]
        public void Cos_SuitLaReference()
        {
            for (int i = -1000; i <= 1000; i++)
            {
                float angle = i * 0.01f;
                Assert.That(Tables.Cos(angle), Is.EqualTo(Math.Cos(angle)).Within(ToleranceSin),
                    $"Cos s'écarte de la référence en {angle}.");
            }
        }

        [Test]
        public void Sin_EstPeriodique()
        {
            for (int i = 0; i < 500; i++)
            {
                float angle = i * 0.013f;
                Assert.That(Tables.Sin(angle + Tables.DeuxPi), Is.EqualTo((double)Tables.Sin(angle)).Within(ToleranceSin));
            }
        }

        [Test]
        public void ExpNeg_SuitLaReferenceEtSatureAuDela()
        {
            Assert.AreEqual(1f, Tables.ExpNeg(0f), 1e-6f);
            for (int i = 0; i <= 800; i++)
            {
                float x = i * 0.01f;
                Assert.That(Tables.ExpNeg(x), Is.EqualTo(Math.Exp(-x)).Within(ToleranceExp),
                    $"ExpNeg s'écarte de la référence en {x}.");
            }
            // Au-delà de la borne, on plafonne au lieu de tomber brutalement à zéro.
            float plafond = Tables.ExpNeg(Tables.ExpMax);
            Assert.AreEqual(plafond, Tables.ExpNeg(50f), 1e-9f);
            Assert.AreEqual(plafond, Tables.ExpNeg(1e30f), 1e-9f);
        }

        [Test]
        public void ExpNeg_EstDecroissante()
        {
            float precedent = Tables.ExpNeg(0f);
            for (int i = 1; i <= 800; i++)
            {
                float v = Tables.ExpNeg(i * 0.01f);
                Assert.LessOrEqual(v, precedent + 1e-7f, "ExpNeg doit être décroissante.");
                precedent = v;
            }
        }

        [Test]
        public void ExpNeg_ArgumentNegatif_RendUn()
        {
            Assert.AreEqual(1f, Tables.ExpNeg(-1f), 1e-6f);
        }

        [Test]
        public void Atan2_SuitLaReferenceDansLesQuatreQuadrants()
        {
            for (int iy = -20; iy <= 20; iy++)
            {
                for (int ix = -20; ix <= 20; ix++)
                {
                    if (ix == 0 && iy == 0) continue;
                    float x = ix * 0.37f;
                    float y = iy * 0.53f;
                    Assert.That(Tables.Atan2(y, x), Is.EqualTo(Math.Atan2(y, x)).Within(ToleranceAtan),
                        $"Atan2 s'écarte de la référence en ({x}, {y}).");
                }
            }
        }

        [Test]
        public void Atan2_CasParticuliers()
        {
            Assert.AreEqual(0f, Tables.Atan2(0f, 0f), 1e-6f);
            Assert.That(Tables.Atan2(0f, 1f), Is.EqualTo(0.0).Within(ToleranceAtan));
            Assert.That(Tables.Atan2(1f, 0f), Is.EqualTo(Math.PI / 2).Within(ToleranceAtan));
            Assert.That(Tables.Atan2(0f, -1f), Is.EqualTo(Math.PI).Within(ToleranceAtan));
            Assert.That(Tables.Atan2(-1f, 0f), Is.EqualTo(-Math.PI / 2).Within(ToleranceAtan));
        }

        [Test]
        public void AucuneEntreeNonFinie_NeProduitDeNaN()
        {
            float[] pieges = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, 0f };
            foreach (float v in pieges)
            {
                Assert.IsFalse(float.IsNaN(Tables.Sin(v)), $"Sin({v}) a produit NaN.");
                Assert.IsFalse(float.IsNaN(Tables.Cos(v)), $"Cos({v}) a produit NaN.");
                Assert.IsFalse(float.IsNaN(Tables.ExpNeg(v)), $"ExpNeg({v}) a produit NaN.");
                foreach (float w in pieges)
                {
                    Assert.IsFalse(float.IsNaN(Tables.Atan2(v, w)), $"Atan2({v}, {w}) a produit NaN.");
                }
            }
        }

        [Test]
        public void BalayageComplet_SansNaNNiInfini()
        {
            for (int i = -5000; i <= 5000; i++)
            {
                float x = i * 0.007f;
                Assert.IsTrue(EstFini(Tables.Sin(x)));
                Assert.IsTrue(EstFini(Tables.Cos(x)));
                Assert.IsTrue(EstFini(Tables.ExpNeg(x)));
                Assert.IsTrue(EstFini(Tables.Atan2(x, 1f - x)));
            }
        }

        static bool EstFini(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
