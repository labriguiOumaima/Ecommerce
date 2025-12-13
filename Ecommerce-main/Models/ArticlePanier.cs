namespace project.Models
{
    public class ArticlePanier
    {
        public int GateauId { get; set; }
        public string NomGateau { get; set; } = "";
        public int Quantite { get; set; }
        public int NombrePersonnes { get; set; }
        public decimal PrixUnitaire { get; set; }
        public decimal PrixBase { get; set; }  
    }
}