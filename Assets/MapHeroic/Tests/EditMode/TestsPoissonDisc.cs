using MapHeroic.Generation.Geometrie;
using MapHeroic.Generation.Noyau;
using NUnit.Framework;
using Unity.Mathematics;

namespace MapHeroic.Tests
{
    /// <summary>
    /// L'échantillonnage conditionne tout le reste : si les sites ne sont pas correctement
    /// espacés, aucune relaxation ne rattrapera des cellules d'aires trop inégales.
    /// </summary>
    public class TestsPoissonDisc
    {
        const float Taille = 300f;
        const float Rayon = 14f;

        static float2[] Echantillon(ulong graine)
        {
            var rng = Rng.DepuisSeed(graine);
            return PoissonDisc.Echantillonner(Taille, Rayon, 20, ref rng);
        }

        [Test]
        public void TousLesPointsSontDansLeDomaine()
        {
            foreach (float2 p in Echantillon(1UL))
            {
                Assert.GreaterOrEqual(p.x, 0f);
                Assert.GreaterOrEqual(p.y, 0f);
                Assert.Less(p.x, Taille);
                Assert.Less(p.y, Taille);
            }
        }

        [Test]
        public void AucunePaireDePointsPlusProcheQueLeRayon()
        {
            float2[] points = Echantillon(2UL);
            float rayonCarre = Rayon * Rayon;
            for (int i = 0; i < points.Length; i++)
            {
                for (int j = i + 1; j < points.Length; j++)
                {
                    float dx = points[i].x - points[j].x;
                    float dy = points[i].y - points[j].y;
                    float d2 = dx * dx + dy * dy;
                    Assert.GreaterOrEqual(d2, rayonCarre - 1e-3f,
                        $"Les points {i} et {j} sont à {math.sqrt(d2):F2} m, moins que le rayon {Rayon} m.");
                }
            }
        }

        [Test]
        public void DensiteConformeALAlgorithmeDeBridson()
        {
            // L'empilement hexagonal donnerait 1,1547 · L²/r² points ; Bridson en atteint
            // environ 62 %. On accepte une fenêtre large, seule une dérive franche compte.
            int points = Echantillon(3UL).Length;
            double maximumTheorique = 1.1547 * Taille * Taille / (Rayon * Rayon);
            double ratio = points / maximumTheorique;
            Assert.That(ratio, Is.InRange(0.50, 0.80),
                $"{points} points, soit {ratio:P0} de l'empilement maximal — hors de la plage attendue.");
        }

        [Test]
        public void MemeGraineMemeNuage()
        {
            float2[] a = Echantillon(42UL);
            float2[] b = Echantillon(42UL);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].x, b[i].x);
                Assert.AreEqual(a[i].y, b[i].y);
            }
        }

        [Test]
        public void GrainesDifferentesNuagesDifferents()
        {
            float2[] a = Echantillon(1UL);
            float2[] b = Echantillon(2UL);
            bool identique = a.Length == b.Length;
            if (identique)
            {
                for (int i = 0; i < a.Length; i++)
                {
                    if (!a[i].Equals(b[i])) { identique = false; break; }
                }
            }
            Assert.IsFalse(identique, "Deux graines doivent donner deux nuages différents.");
        }

        [Test]
        public void CouvertureSansTrouNotable()
        {
            // Tout point du domaine doit avoir un site à moins de 2 rayons : sinon
            // l'échantillonnage a laissé un vide, et il apparaîtrait comme une cellule géante.
            float2[] points = Echantillon(7UL);
            float limite = 2f * Rayon;
            for (float y = 5f; y < Taille; y += 11f)
            {
                for (float x = 5f; x < Taille; x += 11f)
                {
                    float meilleur = float.MaxValue;
                    foreach (float2 p in points)
                    {
                        float dx = p.x - x, dy = p.y - y;
                        float d2 = dx * dx + dy * dy;
                        if (d2 < meilleur) meilleur = d2;
                    }
                    Assert.Less(math.sqrt(meilleur), limite,
                        $"Vide d'échantillonnage autour de ({x}, {y}).");
                }
            }
        }

        [Test]
        public void AnneauEntoureCompletementLeDomaine()
        {
            float2[] anneau = PoissonDisc.Anneau(Taille, 20f, 20f);
            Assert.Greater(anneau.Length, 8);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (float2 p in anneau)
            {
                minX = math.min(minX, p.x); minY = math.min(minY, p.y);
                maxX = math.max(maxX, p.x); maxY = math.max(maxY, p.y);
            }
            Assert.LessOrEqual(minX, -20f + 1e-3f);
            Assert.LessOrEqual(minY, -20f + 1e-3f);
            Assert.GreaterOrEqual(maxX, Taille + 20f - 1e-3f);
            Assert.GreaterOrEqual(maxY, Taille + 20f - 1e-3f);

            // Aucun point de l'anneau ne doit tomber dans le domaine.
            foreach (float2 p in anneau)
            {
                bool dedans = p.x > 0f && p.x < Taille && p.y > 0f && p.y < Taille;
                Assert.IsFalse(dedans, $"Le point d'anneau ({p.x}, {p.y}) est dans le domaine.");
            }
        }
    }
}
