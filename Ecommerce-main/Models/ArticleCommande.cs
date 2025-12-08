namespace project.Models
{
    public class ArticleCommande
    {
        public int Id { get; set; }
        public int CommandeId { get; set; }
        public int GateauId { get; set; }
        public int Quantite { get; set; }
        public decimal PrixUnitaire { get; set; }
    }
}
