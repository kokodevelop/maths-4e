Imports System.Text
Imports System.Threading.RateLimiting
Imports Dapper
Imports Microsoft.AspNetCore.Authentication.JwtBearer
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.IdentityModel.Tokens

''' <summary>
''' API de suivi des élèves de l'application Maths 4e.
''' Base de données : MySQL ou SQL Server (Base:Fournisseur dans appsettings.json).
''' </summary>
Public Module Program

    Public Sub Main(args As String())
        ' Les colonnes snake_case (date_maj) sont lues dans les propriétés PascalCase (DateMaj)
        DefaultTypeMap.MatchNamesWithUnderscores = True

        Dim builder = WebApplication.CreateBuilder(args)
        Dim cfg = builder.Configuration

        Dim cleJwt = cfg("Jwt:Cle")
        If String.IsNullOrWhiteSpace(cleJwt) OrElse cleJwt.Length < 32 Then
            Throw New InvalidOperationException(
                "Jwt:Cle doit contenir au moins 32 caractères. Renseigne-la dans appsettings.Production.json " &
                "ou dans la variable d'environnement Jwt__Cle.")
        End If

        builder.Services.AddSingleton(Of Base)()
        builder.Services.AddSingleton(Of Jetons)()
        builder.Services.AddControllers()

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(
            Sub(o)
                o.MapInboundClaims = False
                o.TokenValidationParameters = New TokenValidationParameters With {
                    .ValidateIssuer = True, .ValidIssuer = Jetons.Emetteur,
                    .ValidateAudience = True, .ValidAudience = Jetons.Audience,
                    .ValidateIssuerSigningKey = True,
                    .IssuerSigningKey = New SymmetricSecurityKey(Encoding.UTF8.GetBytes(cleJwt)),
                    .ValidateLifetime = True, .ClockSkew = TimeSpan.FromMinutes(2),
                    .RoleClaimType = "role", .NameClaimType = "sub"
                }
            End Sub)
        builder.Services.AddAuthorization()

        ' Origines autorisées à appeler l'API (l'application en ligne)
        Dim origines = cfg.GetSection("Cors:Origines").Get(Of String())()
        If origines Is Nothing Then origines = Array.Empty(Of String)()
        builder.Services.AddCors(
            Sub(o) o.AddDefaultPolicy(
                Sub(p) p.WithOrigins(origines).AllowAnyHeader().AllowAnyMethod()))

        ' Limite les tentatives de connexion : 20 par minute et par adresse IP
        builder.Services.AddRateLimiter(
            Sub(o)
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests
                o.AddPolicy(Of String)("connexion",
                    Function(ctx As HttpContext)
                        Dim ip = If(ctx.Connection.RemoteIpAddress?.ToString(), "inconnue")
                        Return RateLimitPartition.GetFixedWindowLimiter(ip,
                            Function(cle) New FixedWindowRateLimiterOptions With {
                                .PermitLimit = 20, .Window = TimeSpan.FromMinutes(1), .QueueLimit = 0})
                    End Function)
            End Sub)

        Dim app = builder.Build()
        app.UseCors()
        app.UseRateLimiter()
        app.UseAuthentication()
        app.UseAuthorization()
        app.MapControllers()
        app.Run()
    End Sub

End Module
