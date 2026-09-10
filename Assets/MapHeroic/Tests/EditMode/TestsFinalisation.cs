using System.Collections.Generic;
using MapHeroic.Generation;
using MapHeroic.Generation.Terrain;
using NUnit.Framework;

namespace MapHeroic.Tests
{
    /// <summary>
    /// Critères J7 : six départs distincts et viables, quotas de terrains tenus, emplacements
    /// de ponts, empreinte reproductible, et les dix-huit règles vérifiées de bout en bout.
    /// </summary>
    public class TestsFinalisation
    {
        const int NbGrainesCampagne = 12;

        static Carte GenererValide(ulong graine, out RapportGeneration rapport)
        {
            var carte = GenerateurCarte.Generer(graine, new ParametresGeneration(), out rapport);
            Assert.IsNotNull(carte, $"Génération échouée pour la graine {graine} : {rapport}");
            return carte;
        }

        // --------------------------------------------------------------- départs

        [Test]
        public void SixDepartsDistinctsEtViables()
        {
            var carte = GenererValide(140UL, out _);
            Assert.AreEqual(6, carte.GroupesDepart.Length);
            Assert.AreEqual(6, carte.ZonesDepart.Length);

            var groupes = new HashSet<int>();
            var zones = new HashSet<int>();
            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(groupes.Add(carte.GroupesDepart[i]), "Deux joueurs partiraient du même groupe.");
                Assert.IsTrue(zones.Add(carte.ZonesDepart[i]), "Deux joueurs partiraient de la même zone.");
                Assert.That(carte.GroupesDepart[i], Is.InRange(0, carte.NbGroupes - 1));
                Assert.That(carte.ZonesDepart[i], Is.InRange(0, carte.NbZones - 1));
                Assert.AreEqual(carte.GroupesDepart[i], carte.GroupeDeZone[carte.ZonesDepart[i]],
                    "La zone de départ doit appartenir au groupe de départ.");
            }
        }

        [Test]
        public void LesZonesDeDepartSontConstructiblesEtNourricieres()
        {
            var carte = GenererValide(141UL, out _);
            foreach (int z in carte.ZonesDepart)
            {
                int constructibles = 0;
                foreach (int c in carte.CellulesDeZone[z])
                {
                    if (!carte.ADrapeau(c, DrapeauxCellule.NonConstructible)) constructibles++;
                }
                float part = (float)constructibles / carte.CellulesDeZone[z].Count;
                Assert.GreaterOrEqual(part, 0.75f, $"La zone de départ {z} n'est constructible qu'à {part:P0}.");

                Assert.AreNotEqual(TypeTerrain.Desert, carte.TerrainDeZone[z], "Un départ ne doit pas être en désert.");
                Assert.AreNotEqual(TypeTerrain.Marais, carte.TerrainDeZone[z], "Un départ ne doit pas être en marais.");
                Assert.AreNotEqual(TypeTerrain.Montagne, carte.TerrainDeZone[z], "Un départ ne doit pas être en montagne.");
            }
        }

        [Test]
        public void AucunTerrainSterileAutourDUnDepart()
        {
            var carte = GenererValide(142UL, out _);
            foreach (int depart in carte.ZonesDepart)
            {
                foreach (int voisine in carte.ZonesVoisines[depart])
                {
                    Assert.AreNotEqual(TypeTerrain.Desert, carte.TerrainDeZone[voisine],
                        $"La zone {voisine}, voisine du départ {depart}, est un désert.");
                    Assert.AreNotEqual(TypeTerrain.Marais, carte.TerrainDeZone[voisine],
                        $"La zone {voisine}, voisine du départ {depart}, est un marais.");
                }
            }
        }

        // -------------------------------------------------------------- terrains

        [Test]
        public void ChaqueZoneAUnTerrainEtLaRessourceQuiVaAvec()
        {
            var carte = GenererValide(143UL, out _);
            Assert.AreEqual(carte.NbZones, carte.TerrainDeZone.Length);
            for (int z = 0; z < carte.NbZones; z++)
            {
                Assert.AreEqual(Terrains.Ressource(carte.TerrainDeZone[z]), carte.RessourceDeZone[z],
                    $"La zone {z} produit une ressource étrangère à son terrain.");
            }
        }

        [Test]
        public void DesertEtMaraisRestentDesMaxima()
        {
            var p = new ParametresGeneration();
            for (int i = 0; i < NbGrainesCampagne; i++)
            {
                GenerateurCarte.Generer(12000UL + (ulong)i, p, out RapportGeneration rapport);
                Assert.IsNotNull(rapport.Terrains, $"Graine {12000 + i} n'a pas atteint les terrains.");
                TestContext.WriteLine($"graine {12000 + i} : {rapport.Terrains}");

                Assert.LessOrEqual(rapport.Terrains.Compte[(int)TypeTerrain.Desert], p.Terrains.MaximumDesert);
                Assert.LessOrEqual(rapport.Terrains.Compte[(int)TypeTerrain.Marais], p.Terrains.MaximumMarais);
            }
        }

        [Test]
        public void LesTerrainsCouvrentToutesLesZonesUneSeuleFois()
        {
            GenererValide(144UL, out RapportGeneration rapport);
            int total = 0;
            foreach (int n in rapport.Terrains.Compte) total += n;
            Assert.AreEqual(90, total, "La somme des terrains doit retomber sur 90 zones.");
        }

        [Test]
        public void LesZonesMontagneSontEnNombreRaisonnable()
        {
            GenererValide(145UL, out RapportGeneration rapport);
            int montagnes = rapport.Terrains.Compte[(int)TypeTerrain.Montagne];
            Assert.That(montagnes, Is.InRange(1, 12),
                $"{montagnes} zones de montagne : le minerai serait soit introuvable, soit omniprésent.");
        }

        // ----------------------------------------------------------------- ponts

        [Test]
        public void LesPontsEnjambentDesFrontieresSansPassage()
        {
            var carte = GenererValide(146UL, out _);
            Assert.Greater(carte.Ponts.Count, 0, "Aucun emplacement de pont : les rivières seraient infranchissables.");

            foreach (EmplacementPont pont in carte.Ponts)
            {
                Assert.AreNotEqual(pont.GroupeA, pont.GroupeB);
                Assert.AreEqual(2, pont.Aretes.Length);
                foreach (Passage passage in carte.Passages)
                {
                    bool memeFrontiere = (passage.GroupeA == pont.GroupeA && passage.GroupeB == pont.GroupeB)
                                      || (passage.GroupeA == pont.GroupeB && passage.GroupeB == pont.GroupeA);
                    Assert.IsFalse(memeFrontiere,
                        $"Un pont doublonne le passage entre les groupes {pont.GroupeA} et {pont.GroupeB}.");
                }
            }
        }

        // -------------------------------------------------------------- MapData

        [Test]
        public void MapDataSeReconstruitAlIdentique()
        {
            var p = new ParametresGeneration();
            var carte = GenererValide(147UL, out _);
            MapData data = MapData.Depuis(carte, p.Maillage.TailleCarte);

            Assert.AreEqual(data.Empreinte, data.CalculerEmpreinte(), "L'empreinte doit être stable.");

            // Un client qui reçoit la carte ne dispose que des tables sérialisées.
            var graphe = data.ReconstruireGraphe();
            Assert.AreEqual(carte.Graphe.NbAretes, graphe.NbAretes, "Le maillage reconstruit doit avoir les mêmes arêtes.");
            CollectionAssert.AreEqual(carte.Graphe.VoisinsDeCellule, graphe.VoisinsDeCellule);
            CollectionAssert.AreEqual(carte.Graphe.AreteCelluleA, graphe.AreteCelluleA);
            CollectionAssert.AreEqual(carte.Graphe.CoinsVoisins, graphe.CoinsVoisins);
        }

        [Test]
        public void MemeGraine_MemeEmpreinte()
        {
            var p = new ParametresGeneration();
            var a = MapData.Depuis(GenererValide(148UL, out _), p.Maillage.TailleCarte);
            var b = MapData.Depuis(GenererValide(148UL, out _), p.Maillage.TailleCarte);
            var c = MapData.Depuis(GenererValide(149UL, out _), p.Maillage.TailleCarte);

            Assert.AreEqual(a.Empreinte, b.Empreinte, "Même graine, même empreinte.");
            Assert.AreEqual(a.EmpreinteAltitudes, b.EmpreinteAltitudes);
            Assert.AreNotEqual(a.Empreinte, c.Empreinte, "Deux graines doivent donner deux empreintes.");
        }

        [Test]
        public void MapDataTientDansLeBudgetDeTaille()
        {
            var p = new ParametresGeneration();
            var carte = GenererValide(150UL, out _);
            MapData data = MapData.Depuis(carte, p.Maillage.TailleCarte);
            long ko = data.TailleApproximative() / 1024;
            TestContext.WriteLine($"MapData ≈ {ko} Ko avant compression");
            Assert.Less(ko, 700, "MapData dépasse le budget d'envoi de secours.");
        }

        [Test]
        public void LesAltitudesTiennentDansUnEntierCourt()
        {
            var p = new ParametresGeneration();
            var carte = GenererValide(151UL, out _);
            MapData data = MapData.Depuis(carte, p.Maillage.TailleCarte);
            for (int i = 0; i < data.NbCoins; i++)
            {
                Assert.AreEqual(carte.HauteurCoin[i], data.Altitude(i), 0.03f,
                    $"L'altitude du coin {i} ne survit pas à la quantification.");
            }
        }

        // ------------------------------------------------------------ validation

        [Test]
        public void Campagne_ToutesLesReglesSontRespectees()
        {
            var p = new ParametresGeneration();
            var echecs = new Dictionary<string, int>();

            for (int i = 0; i < NbGrainesCampagne; i++)
            {
                ulong graine = 13000UL + (ulong)i;
                var carte = GenerateurCarte.Generer(graine, p, out RapportGeneration rapport);
                Assert.IsNotNull(carte, $"Graine {graine} : {rapport}");

                foreach (ResultatRegle regle in Validation.Verifier(carte, p))
                {
                    if (regle.Conforme) continue;
                    echecs.TryGetValue(regle.Code, out int n);
                    echecs[regle.Code] = n + 1;
                    TestContext.WriteLine($"graine {graine} : {regle}");
                }
            }

            Assert.AreEqual(0, echecs.Count,
                $"Règles enfreintes : {string.Join(", ", echecs.Keys)}");
        }

        [Test]
        public void LeRapportDeValidationEstComplet()
        {
            var p = new ParametresGeneration();
            var carte = GenererValide(152UL, out _);
            List<ResultatRegle> regles = Validation.Verifier(carte, p);

            Assert.AreEqual(18, regles.Count, "Les dix-huit règles doivent être rapportées.");
            for (int i = 0; i < regles.Count; i++)
            {
                Assert.AreEqual($"R{i + 1}", regles[i].Code, "Les règles doivent être dans l'ordre.");
                Assert.IsNotEmpty(regles[i].Mesure, $"{regles[i].Code} ne rapporte aucune mesure.");
                TestContext.WriteLine(regles[i].ToString());
            }
        }
    }
}
