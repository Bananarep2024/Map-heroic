using System;
using System.Diagnostics;
using MapHeroic.Generation.Geometrie;
using MapHeroic.Generation.Noyau;
using MapHeroic.Generation.Terrain;

namespace MapHeroic.Generation
{
    public sealed class ParametresGeneration
    {
        public ParametresMaillage Maillage = new ParametresMaillage();
        public ParametresIle Ile = new ParametresIle();
        public ParametresRelief Relief = new ParametresRelief();
        public ParametresHydrologie Hydrologie = new ParametresHydrologie();
        public ParametresZones Zones = new ParametresZones();
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
        public long MillisecondesTotal;

        public override string ToString()
        {
            if (!Reussi) return $"ÉCHEC : {MotifEchec}\n  {Maillage}\n  {Ile}\n  {Zones}";
            return $"{MillisecondesTotal} ms\n  {Maillage}\n  {Ile}\n  {Relief}\n  {Hydrologie}\n  {Zones}";
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

            var racine = Rng.DepuisSeed(graine);

            if (!Ile.Construire(carte, p.Ile, racine, out DiagnosticIle diagIle))
            {
                rapport.Ile = diagIle;
                rapport.MotifEchec = diagIle.MotifEchec;
                rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
                return null;
            }
            rapport.Ile = diagIle;

            Relief.Construire(carte, p.Relief, racine, out DiagnosticRelief diagRelief);
            rapport.Relief = diagRelief;

            Hydrologie.Construire(carte, p.Hydrologie, racine, out DiagnosticHydrologie diagHydro);
            rapport.Hydrologie = diagHydro;

            if (!Zones.Construire(carte, p.Zones, racine, out DiagnosticZones diagZones))
            {
                rapport.Zones = diagZones;
                rapport.MotifEchec = diagZones.MotifEchec;
                rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
                return null;
            }
            rapport.Zones = diagZones;

            rapport.Reussi = true;
            rapport.MillisecondesTotal = chrono.ElapsedMilliseconds;
            return carte;
        }
    }
}
