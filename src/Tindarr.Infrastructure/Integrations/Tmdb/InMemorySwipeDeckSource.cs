using Tindarr.Application.Interfaces.Interactions;
using Tindarr.Domain.Common;
using Tindarr.Domain.Interactions;

namespace Tindarr.Infrastructure.Integrations.Tmdb;

public sealed class InMemorySwipeDeckSource : ISwipeDeckSource
{
    private static readonly IReadOnlyList<SwipeCard> Cards = new List<SwipeCard>
    {
        new(550, "Fight Club", "An insomniac office worker and a devil-may-care soap maker form an underground fight club.", "https://image.tmdb.org/t/p/w500/bptfVGEQuv6vDTIMVCHjJ9Dz8PX.jpg", "https://image.tmdb.org/t/p/w780/hZkgoQYus5vegHoetLkCJzb17zJ.jpg", 1999, 8.4),
        new(603, "The Matrix", "A computer hacker learns about the true nature of his reality and his role in the war.", "https://image.tmdb.org/t/p/w500/f89U3ADr1oiB1s9GkdPOEpXUk5H.jpg", "https://image.tmdb.org/t/p/w780/8uO0gUM8aNqYLs1OsTBQiXu0fEv.jpg", 1999, 8.2),
        new(155, "The Dark Knight", "Batman faces the Joker, a criminal mastermind who wants to plunge Gotham City into anarchy.", "https://image.tmdb.org/t/p/w500/qJ2tW6WMUDux911r6m7haRef0WH.jpg", "https://image.tmdb.org/t/p/w780/hZkgoQYus5vegHoetLkCJzb17zJ.jpg", 2008, 8.5),
        new(680, "Pulp Fiction", "The lives of two mob hitmen, a boxer, and others intertwine.", "https://image.tmdb.org/t/p/w500/d5iIlFn5s0ImszYzBPb8JPIfbXD.jpg", "https://image.tmdb.org/t/p/w780/suaEOtk1N1sgg2MTM7oZd2cfVp3.jpg", 1994, 8.5),
        new(13, "Forrest Gump", "Forrest Gump, a man with a low IQ, recounts the early years of his life.", "https://image.tmdb.org/t/p/w500/saHP97rTPS5eLmrLQEcANmKrsFl.jpg", "https://image.tmdb.org/t/p/w780/qdIMHd4sEfJSckfVJfKQvisL02a.jpg", 1994, 8.5),
        new(278, "The Shawshank Redemption", "Two imprisoned men bond over a number of years.", "https://image.tmdb.org/t/p/w500/q6y0Go1tsGEsmtFryDOJo3dEmqu.jpg", "https://image.tmdb.org/t/p/w780/iNh3BivHyg5sQRPP1KOkzguEX0H.jpg", 1994, 8.7),
        new(122, "The Lord of the Rings: The Return of the King", "The final confrontation in Middle-earth.", "https://image.tmdb.org/t/p/w500/rCzpDGLbOoPwLjy3OAm5NUPOTrC.jpg", "https://image.tmdb.org/t/p/w780/9RrLz2fQ2whB1lzoQH0Fmmxk1Y.jpg", 2003, 8.5),
        new(1891, "The Empire Strikes Back", "The Rebels are pursued by the Empire in the aftermath of the Death Star's destruction.", "https://image.tmdb.org/t/p/w500/7BuH8itoSrLExs2YZSsM01Qk2no.jpg", "https://image.tmdb.org/t/p/w780/2u7zbn8tNrEWfY9VP2K4QQ9nN8K.jpg", 1980, 8.4)
    };

    public Task<IReadOnlyList<SwipeCard>> GetCandidatesAsync(string userId, ServiceScope scope, CancellationToken cancellationToken)
    {
        return Task.FromResult(Cards);
    }
}
