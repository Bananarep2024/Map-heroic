using System;
using System.Diagnostics;
using MapHeroic.Generation.Geometrie;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;

namespace MapHeroic.Generation
{
    /// <summary>Étape à laquelle arrêter la génération.</summary>
    public enum PhaseGeneration
    {
        Maillage,
        Ile,
        Relief,
        Hydrologie,
        Zones,
        Groupes,
        Passages,
        Materialisation,
        Departs,
        Terrains,
        Complet
    }

    public sealed class ParametresGeneration
    {
        /// <summary>
        /// Arrête la génération après cette phase. Sert à vérifier les invariants d'une phase
        /// avant que les suivantes ne les modifient légitimement — la matérialisation creuse
        /// les lits et surélève les massifs, si bien que les altitudes de l'hydrologie ne
        /// sont plus celles de la fin du pipeline. L'éditeur s'en sert aussi pour afficher
        /// l'état intermédiaire de chaque calque.
        /// </summary>
        public PhaseGeneration PhaseFinale = PhaseGeneration.Complet;

        public ParametresMaillage Maillage = new ParametresMaillage();
        public ParametresIle Ile = new ParametresIle();
        public ParametresRelief Relief = new ParametresRelief();
        public ParametresHydrologie Hydrologie = new ParametresHydrologie();
        public ParametresZones Zones = new ParametresZones();
        public ParametresGroupes Groupes = new ParametresGroupes();
        public ParametresPassages Passages = new ParametresPassages();
        public ParametresMaterialisation Materialisation = new ParametresMaterialisation();
        public ParametresDeparts Departs = new ParametresDeparts();
        public ParametresTerrains Terrains = new ParametresTerrains();
    }

    public sealed class RapportGeneration
    {
        public bool Reussi;
        public string MotifEchec;
        public DiagnosticMaillage Maillage;
        public DiagnosticIle Ile;
        public DiagnosticRelief Relief;
        public DiagnosticHydrologie Hydrologie;
        public DiagnosticZones Zones;
        public DiagnosticGroupes Groupes;
        public DiagnosticPassages Passages;
        public DiagnosticMaterialisation Materialisation;
        public DiagnosticDeparts Departs;
        public DiagnosticTerrains Terrains;
        public long MillisecondesTotal;

        public override string ToString()
        {
            if (!Reussi)
            {
                return $"ÉCHEC : {MotifEchec}\n  {Maillage}\n  {Ile}\n  {Zones}\n  {Groupes}\n  " +
                       $"{Passages}\n  {Materialisation}";
            }
            return $"{MillisecondesTotal} ms\n  {Maillage}\n  {Ile}\n  {Relief}\n  {Hydrologie}\n  " +
                   $"{Zones}\n  {Groupes}\n  {Passages}\n  {Materialisation}\n  {Departs}\n  {Terrains}";
        }
    }

    /// <summary>
    /// Enchaîne les phases du générateur. Chaque phase ne lit que les sorties des
    /// précédentes et écrit les siennes dans la même <see cref="Carte"/> ; aucune ne revient
    /// sur ce qu'une autre a produit.
    ///
    /// Aucune API du moteur n'est appelée ici : la génération peut tourner hors du thread
    /// principal, et les tests s'exécutent sans ouvrir de scène.
    /// </summary>
    public static class GenerateurCarte
    {
        /// <summary>Renvoie null si aucune île acceptable n'a pu être tirée pour cette graine.</summary>
        public static Carte Generer(ulong graine, ParametresGeneration p, out RapportGeneration rapport)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));

            // La taille du domaine est portée par les paramètres de maillage : on la propage
            // plutôt que de laisser deux réglages diverger en silence.
            p.Ile.TailleCarte = p.Maillage.TailleCarte;

            rapport = new RapportGeneration();
            var chrono = Stopwatch.StartNew();

            var carte = new Carte
            {
                Graine = graine,
                Graphe = ConstructeurMaillage.Construire(graine, p.Maillage, out DiagnosticMaillage diagMaillage)
            };
            rapport.Maillage = diagMaillage;
            if (p.PhaseFinale == PhaseGeneration.Maillage) return Terminer(rapport, carte, chrono);

            var racine = Rng.DepuisSeed(graine);

            if (!Ile.Construire(carte, p.Ile, racine, out DiagnosticIle diagIle))
            {
                rapport.Ile = diagIle;
                rapport.MotifEchec = diagIle.MotifEchec;
                rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
                return null;
            }
            rapport.Ile = diagIle;
            if (p.PhaseFinale == PhaseGeneration.Ile) return Terminer(rapport, carte, chrono);

            Relief.Construire(carte, p.Relief, racine, out DiagnosticRelief diagRelief);
            rapport.Relief = diagRelief;
            if (p.PhaseFinale == PhaseGeneration.Relief) return Terminer(rapport, carte, chrono);

            Hydrologie.Construire(carte, p.Hydrologie, racine, out DiagnosticHydrologie diagHydro);
            rapport.Hydrologie = diagHydro;
            if (p.PhaseFinale == PhaseGeneration.Hydrologie) return Terminer(rapport, carte, chrono);

            if (!Zones.Construire(carte, p.Zones, racine, out DiagnosticZones diagZones))
            {
                rapport.Zones = diagZones;
                rapport.MotifEchec = diagZones.MotifEchec;
                rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
                return null;
            }
            rapport.Zones = diagZones;
            if (p.PhaseFinale == PhaseGeneration.Zones) return Terminer(rapport, carte, chrono);

            if (!Groupes.Construire(carte, p.Groupes, racine, out DiagnosticGroupes diagGroupes))
            {
                rapport.Groupes = diagGroupes;
                rapport.MotifEchec = diagGroupes.MotifEchec;
                rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
                return null;
            }
            rapport.Groupes = diagGroupes;
            if (p.PhaseFinale == PhaseGeneration.Groupes) return Terminer(rapport, carte, chrono);

            if (!Passages.Construire(carte, p.Passages, racine, out DiagnosticPassages diagPassages))
            {
                rapport.Passages = diagPassages;
                rapport.MotifEchec = diagPassages.MotifEchec;
                rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
                return null;
            }
            rapport.Passages = diagPassages;
            if (p.PhaseFinale == PhaseGeneration.Passages) return Terminer(rapport, carte, chrono);

            if (!Materialisation.Construire(carte, p.Materialisation, racine, out DiagnosticMaterialisation diagMat))
            {
                rapport.Materialisation = diagMat;
                rapport.MotifEchec = diagMat.MotifEchec;
                rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
                return null;
            }
            rapport.Materialisation = diagMat;
            if (p.PhaseFinale == PhaseGeneration.Materialisation) return Terminer(rapport, carte, chrono);

            if (!Departs.Construire(carte, p.Departs, racine, out DiagnosticDeparts diagDeparts))
            {
                rapport.Departs = diagDeparts;
                rapport.MotifEchec = diagDeparts.MotifEchec;
                rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
                return null;
            }
            rapport.Departs = diagDeparts;
            if (p.PhaseFinale == PhaseGeneration.Departs) return Terminer(rapport, carte, chrono);

            if (!AffectationTerrains.Construire(carte, p.Terrains, racine, out DiagnosticTerrains diagTerrains))
            {
                rapport.Terrains = diagTerrains;
                rapport.MotifEchec = diagTerrains.MotifEchec;
                rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
                return null;
            }
            rapport.Terrains = diagTerrains;

            return Terminer(rapport, carte, chrono);
        }

        static Carte Terminer(RapportGeneration rapport, Carte carte, Stopwatch chrono)
        {
            rapport.Reussi = true;
            rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
            return carte;
        }
    }
}
