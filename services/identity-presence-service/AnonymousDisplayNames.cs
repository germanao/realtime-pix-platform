using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

public static class AnonymousDisplayNames
{
    public static IReadOnlyList<string> FootballWorldCup2026 { get; } =
    [
        "Lionel Messi",
        "Kylian Mbappe",
        "Erling Haaland",
        "Harry Kane",
        "Lamine Yamal",
        "Neymar",
        "Cristiano Ronaldo",
        "Christian Pulisic",
        "Jonathan David",
        "Mohamed Salah",
        "Sadio Mane",
        "Brahim Diaz",
        "Roberto Alvarado",
        "Kai Havertz",
        "Cody Gakpo",
        "Darwin Nunez",
        "Luis Diaz",
        "Takefusa Kubo",
        "Son Heung-min",
        "Jeremy Doku",
        "Andrej Kramaric",
        "Alexander Isak",
        "Arda Guler",
        "Riyad Mahrez",
        "Lyle Foster",
        "Scott McTominay",
        "Breel Embolo",
        "Sebastien Haller",
        "Mohammed Kudus",
        "Marko Arnautovic"
    ];

    public static IReadOnlyList<string> Nfl { get; } =
    [
        "Patrick Mahomes",
        "Josh Allen",
        "Lamar Jackson",
        "Joe Burrow",
        "Matthew Stafford",
        "Jalen Hurts",
        "Justin Herbert",
        "Dak Prescott",
        "Jared Goff",
        "Drake Maye",
        "Caleb Williams",
        "Trevor Lawrence",
        "Baker Mayfield",
        "Jordan Love",
        "Tua Tagovailoa",
        "C.J. Stroud",
        "Aaron Rodgers",
        "Saquon Barkley",
        "Christian McCaffrey",
        "Derrick Henry",
        "Jonathan Taylor",
        "Bijan Robinson",
        "James Cook",
        "Ja'Marr Chase",
        "Justin Jefferson",
        "Puka Nacua",
        "Amon-Ra St. Brown",
        "CeeDee Lamb",
        "Nico Collins",
        "Travis Kelce"
    ];

    public static IReadOnlyList<string> Nba { get; } =
    [
        "Scottie Barnes",
        "Devin Booker",
        "Cade Cunningham",
        "Jalen Duren",
        "Anthony Edwards",
        "Chet Holmgren",
        "Jalen Johnson",
        "Tyrese Maxey",
        "Jaylen Brown",
        "Jalen Brunson",
        "Kevin Durant",
        "De'Aaron Fox",
        "Brandon Ingram",
        "LeBron James",
        "Kawhi Leonard",
        "Donovan Mitchell",
        "Stephen Curry",
        "Deni Avdija",
        "Luka Doncic",
        "Nikola Jokic",
        "Jamal Murray",
        "Norman Powell",
        "Alperen Sengun",
        "Pascal Siakam",
        "Karl-Anthony Towns",
        "Victor Wembanyama",
        "Giannis Antetokounmpo",
        "Shai Gilgeous-Alexander",
        "Cooper Flagg",
        "Derrick White"
    ];

    public static IReadOnlyList<string> Suffixes { get; } =
    [
        "Ledger",
        "Signal",
        "Vector",
        "Circuit",
        "Harbor",
        "Pixel",
        "Matrix",
        "Quartz"
    ];

    public static IReadOnlyList<string> AthleteNames { get; } =
        FootballWorldCup2026.Concat(Nfl).Concat(Nba).ToArray();

    public static string Create(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(clientId));
        var athleteIndex = BinaryPrimitives.ReadUInt32LittleEndian(hash) % (uint)AthleteNames.Count;
        var suffixIndex = hash[4] % Suffixes.Count;
        return $"{AthleteNames[(int)athleteIndex]} {Suffixes[suffixIndex]}";
    }
}

