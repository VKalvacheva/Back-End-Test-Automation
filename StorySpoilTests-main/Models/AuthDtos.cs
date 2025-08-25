namespace StorySpoilTests.Models;

public class CreateUserRequestDto
{
    public string? UserName { get; set; }
    public string? FirstName { get; set; }
    public string? MidName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? RePassword { get; set; }
}

public class LoginRequestDto
{
    public string? UserName { get; set; }
    public string? Password { get; set; }
}

public class AuthResponseDto
{
    public string? AccessToken { get; set; }
}