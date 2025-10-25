using System.ComponentModel.DataAnnotations.Schema;

namespace Underground.OutboxTest.TestHandler;

[Table("Users")]
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
