using Microsoft.AspNetCore.Mvc;
using project.Models;
using Services;
using Neo4j.Driver;

namespace project.Controllers
{
    public class GateauController : Controller
    {
        private readonly Neo4jService _neo4jService;
        private readonly ILogger<GateauController> _logger;

        public GateauController(Neo4jService neo4jService, ILogger<GateauController> logger)
        {
            _neo4jService = neo4jService;
            _logger = logger;
        }

        // GET: Gateau/Index - Page d'ACCUEIL (4 gâteaux)
        public async Task<IActionResult> Index()
        {
            var gateaux = new List<Gateau>();

            try
            {
                var session = _neo4jService.GetAsyncSession();

                var query = @"
                    MATCH (g:Gateau)
                    RETURN 
                        g.id as Id,
                        g.nom as Nom,
                        g.description as Description,
                        g.prix as Prix,
                        COALESCE(g.stock, 1) as Stock,
                        g.categorie as Categorie
                    ORDER BY g.nom
                    LIMIT 4"; // Limite à 4 gâteaux pour l'accueil

                var result = await session.RunAsync(query);

                await foreach (var record in result)
                {
                    var stock = record["Stock"].As<int>();
                    if (stock > 0)
                    {
                        gateaux.Add(new Gateau
                        {
                            Id = record["Id"].As<int>(),
                            Nom = record["Nom"].As<string>() ?? "",
                            Description = record["Description"].As<string>() ?? "",
                            Prix = record["Prix"].As<decimal>(),
                            Stock = stock,
                            Categorie = record["Categorie"].As<string>() ?? ""
                        });
                    }
                }

                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur: {ex.Message}");
            }

            return View(gateaux);
        }

        // GET: Gateau/Catalogue - TOUS les gâteaux
        public async Task<IActionResult> Catalogue()
        {
            var gateaux = new List<Gateau>();

            try
            {
                var session = _neo4jService.GetAsyncSession();

                var query = @"
                    MATCH (g:Gateau)
                    RETURN 
                        g.id as Id,
                        g.nom as Nom,
                        g.description as Description,
                        g.prix as Prix,
                        COALESCE(g.stock, 1) as Stock,
                        g.categorie as Categorie
                    ORDER BY g.nom"; // PAS DE LIMIT = TOUS les gâteaux !

                var result = await session.RunAsync(query);

                await foreach (var record in result)
                {
                    var stock = record["Stock"].As<int>();
                    if (stock > 0)
                    {
                        gateaux.Add(new Gateau
                        {
                            Id = record["Id"].As<int>(),
                            Nom = record["Nom"].As<string>() ?? "",
                            Description = record["Description"].As<string>() ?? "",
                            Prix = record["Prix"].As<decimal>(),
                            Stock = stock,
                            Categorie = record["Categorie"].As<string>() ?? ""
                        });
                    }
                }

                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur: {ex.Message}");
            }

            return View(gateaux);
        }

        // GET: Gateau/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var session = _neo4jService.GetAsyncSession();

                var query = @"
                    MATCH (g:Gateau)
                    WHERE g.id = $id
                    RETURN 
                        g.id as Id,
                        g.nom as Nom,
                        g.description as Description,
                        g.prix as Prix,
                        COALESCE(g.stock, 0) as Stock,
                        g.categorie as Categorie
                    LIMIT 1";

                var result = await session.RunAsync(query, new { id });

                if (!await result.FetchAsync())
                {
                    await session.DisposeAsync();
                    return NotFound();
                }

                var record = result.Current;
                var gateau = new Gateau
                {
                    Id = record["Id"].As<int>(),
                    Nom = record["Nom"].As<string>() ?? "",
                    Description = record["Description"].As<string>() ?? "",
                    Prix = record["Prix"].As<decimal>(),
                    Stock = record["Stock"].As<int>(),
                    Categorie = record["Categorie"].As<string>() ?? ""
                };

                await session.DisposeAsync();
                return View(gateau);
            }
            catch
            {
                return NotFound();
            }
        }
    }
}