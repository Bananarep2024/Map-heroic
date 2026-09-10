namespace MapHeroic.Generation.Terrain
{
    /// <summary>
    /// Frontière entre deux zones voisines : ses arêtes fines et ce qu'elles valent.
    ///
    /// Toutes les grandeurs sont pondérées par la longueur, pas comptées par arête : une
    /// frontière de dix arêtes courtes ne doit pas peser plus qu'une frontière de trois
    /// arêtes longues couvrant la même distance.
    /// </summary>
    public sealed class AreteZones
    {
        public int ZoneA;
        public int ZoneB;

        /// <summary>Arêtes fines composant la frontière, dans l'ordre croissant d'index.</summary>
        public int[] AretesFines;

        /// <summary>Longueur cumulée, en mètres.</summary>
        public float Longueur;

        /// <summary>Dureté moyenne pondérée par la longueur, dans [0, 1].</summary>
        public float Durete;

        /// <summary>
        /// Composante « crête » seule de la dureté. La dureté totale confond crête et
        /// rivière ; les groupes doivent les distinguer, car une rivière intérieure coupe un
        /// groupe en deux alors qu'une crête ne fait que le rendre moins commode.
        /// </summary>
        public float Crete;

        /// <summary>Longueur cumulée des arêtes portant un lit de rivière.</summary>
        public float LongueurRiviere;

        /// <summary>Vrai si la frontière longe la mer sur toute sa longueur.</summary>
        public bool Cotiere;
    }
}
