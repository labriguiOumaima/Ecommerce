namespace project.Models
{
    public class Commande
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public DateTime DateCommande { get; set; }
        public decimal MontantTotal { get; set; }
        public string Statut { get; set; } // "en attente", "confirmée", "livrée"
        public List<ArticleCommande> Articles { get; set; } = new List<ArticleCommande>();//  Liste des articles
    }
}
