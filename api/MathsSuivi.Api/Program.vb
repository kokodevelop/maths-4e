Imports System.IO
Imports System.Text
Imports System.Threading.RateLimiting
Imports Microsoft.AspNetCore.HttpOverrides
Imports Microsoft.AspNetCore.StaticFiles
Imports Microsoft.Extensions.FileProviders
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

        ' Limite les tentatives de connexion par adresse IP et par minute.
        ' Toute une classe peut partager la même adresse publique (wifi du collège) : 60 par défaut.
        Dim limiteConnexions = cfg.GetValue(Of Integer)("Securite:ConnexionsParMinute", 60)
        builder.Services.AddRateLimiter(
            Sub(o)
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests
                o.AddPolicy(Of String)("connexion",
                    Function(ctx As HttpContext)
                        Dim ip = If(ctx.Connection.RemoteIpAddress?.ToString(), "inconnue")
                        Return RateLimitPartition.GetFixedWindowLimiter(ip,
                            Function(cle) New FixedWindowRateLimiterOptions With {
                                .PermitLimit = limiteConnexions, .Window = TimeSpan.FromMinutes(1), .QueueLimit = 0})
                    End Function)
            End Sub)

        ' Derrière un relais local (Tailscale Funnel, Nginx…) : l'adresse IP réelle est dans X-Forwarded-For.
        ' Seuls les relais sur la machine elle-même (127.0.0.1, ::1) sont crus.
        ' Dans Docker, le relais arrive par le réseau interne de Docker : ses plages sont
        ' ajoutées dans Securite:RelaisDeConfiance (ex. "172.16.0.0/12").
        Dim relais = cfg.GetSection("Securite:RelaisDeConfiance").Get(Of String())()
        builder.Services.Configure(Of ForwardedHeadersOptions)(
            Sub(o)
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor Or ForwardedHeaders.XForwardedProto
                If relais IsNot Nothing Then
                    For Each plage In relais
                        Dim morceaux = plage.Split("/"c)
                        o.KnownNetworks.Add(New IPNetwork(Net.IPAddress.Parse(morceaux(0)), Integer.Parse(morceaux(1))))
                    Next
                End If
            End Sub)

        Dim app = builder.Build()
        app.UseForwardedHeaders()

        ' Optionnel : sert aussi l'application web (index.html, prof.html…) depuis Site:Dossier,
        ' pour avoir le site et l'API à la même adresse sur un seul serveur.
        Dim dossierSite = cfg("Site:Dossier")
        If Not String.IsNullOrWhiteSpace(dossierSite) Then
            If Not Directory.Exists(dossierSite) Then
                Throw New InvalidOperationException($"Site:Dossier introuvable : {dossierSite}")
            End If
            Dim fichiers As New PhysicalFileProvider(Path.GetFullPath(dossierSite))
            Dim types As New FileExtensionContentTypeProvider()
            types.Mappings(".webmanifest") = "application/manifest+json"
            app.UseDefaultFiles(New DefaultFilesOptions With {.FileProvider = fichiers})
            app.UseStaticFiles(New StaticFileOptions With {
                .FileProvider = fichiers, .ContentTypeProvider = types,
                .OnPrepareResponse = Sub(c)
                                         ' Pages, config et service worker : toujours revalidés pour recevoir les mises à jour
                                         Dim nom = c.File.Name
                                         If nom.EndsWith(".html") OrElse nom = "sw.js" OrElse nom = "config.js" OrElse nom.EndsWith(".webmanifest") Then
                                             c.Context.Response.Headers.CacheControl = "no-cache"
                                         End If
                                     End Sub})
        End If

        app.UseCors()
        app.UseRateLimiter()
        app.UseAuthentication()
        app.UseAuthorization()
        app.MapControllers()
        app.Run()
    End Sub

End Module
