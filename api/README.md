# API de suivi des élèves — Maths 4e

API ASP.NET Core 8 écrite en **VB.NET**, avec Dapper. Elle tourne aussi sur .NET 9 et 10 (`RollForward=Major`). Elle enregistre les résultats des élèves et fournit le bilan de chaque classe à l'enseignant.
Base de données au choix : **MySQL 8** ou **SQL Server 2016+**.

## Ce que fait l'API

- **Enseignants** : création de compte (avec un code d'invitation), classes, ajout d'élèves, bilan de classe, détail question par question.
- **Élèves** : connexion avec *code de classe + identifiant + code PIN à 4 chiffres*, envoi et récupération de leur progression (ils la retrouvent sur n'importe quelle tablette).
- **Contrôle des résultats côté serveur** (`Services/Regles.vb`) :
  - la première réponse à un QCM est définitive ;
  - les essais ratés, l'indice utilisé et la correction vue ne peuvent jamais être effacés par l'élève ;
  - une question n'est plus réussie si la correction a été vue avant, ou après 3 essais ratés ;
  - barème : 1 point du premier coup sans indice, ½ point sinon.
- **Sécurité** : mots de passe et PIN hachés (BCrypt), jetons JWT, compte élève bloqué 15 min après 5 PIN faux, 60 tentatives de connexion par minute et par adresse IP (adresse réelle lue derrière un relais local), un enseignant ne voit que ses classes.

## Structure

```
api/
├── sql/
│   ├── 01_schema_mysql.sql        création de la base MySQL + chapitres
│   └── 01_schema_sqlserver.sql    création de la base SQL Server + chapitres
└── MathsSuivi.Api/
    ├── Program.vb                 démarrage, JWT, CORS, limitation des connexions
    ├── appsettings.json           configuration (sans secret)
    ├── Donnees/Base.vb            connexion MySQL ou SQL Server
    ├── Modeles/Modeles.vb         objets échangés en JSON
    ├── Services/Jetons.vb         jetons JWT, codes PIN, codes de classe
    ├── Services/Regles.vb         règles de contrôle des résultats
    └── Controleurs/
        ├── AuthController.vb       connexions enseignant / élève
        ├── ClassesController.vb    classes, élèves, bilans (enseignant)
        ├── ProgressionController.vb résultats de l'élève connecté
        └── SanteController.vb      GET /api/sante
```

## Installation

### 1. Base de données

**MySQL**
```bash
mysql -u root -p < api/sql/01_schema_mysql.sql
```
```sql
CREATE USER 'maths4e'@'localhost' IDENTIFIED BY 'un-mot-de-passe-solide';
GRANT SELECT, INSERT, UPDATE, DELETE ON maths4e.* TO 'maths4e'@'localhost';
```

**SQL Server** : ouvrir `api/sql/01_schema_sqlserver.sql` dans SSMS et l'exécuter, ou :
```bash
sqlcmd -S localhost -E -f 65001 -i api/sql/01_schema_sqlserver.sql
```
Donner ensuite à l'utilisateur de l'API (compte du pool IIS ou login SQL) les droits `db_datareader` et `db_datawriter` sur `maths4e`.

Les deux scripts peuvent être relancés sans risque : ils ne recréent pas les tables existantes et mettent à jour la liste des chapitres.

### 2. Configuration

Créer `MathsSuivi.Api/appsettings.Production.json` (ce fichier est ignoré par Git, il contient les secrets) :

```json
{
  "Base": { "Fournisseur": "MySql" },
  "ConnectionStrings": {
    "MySql": "Server=localhost;Port=3306;Database=maths4e;User ID=maths4e;Password=un-mot-de-passe-solide;",
    "SqlServer": "Server=MONSERVEUR;Database=maths4e;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Jwt": { "Cle": "une-longue-chaine-aleatoire-d-au-moins-32-caracteres" },
  "Inscription": { "CodeInvitation": "un-code-a-donner-aux-collegues" },
  "Cors": { "Origines": [ "https://maths-4e-sigma.vercel.app" ] }
}
```

| Réglage | Rôle |
|---|---|
| `Base:Fournisseur` | `MySql` ou `SqlServer` |
| `Jwt:Cle` | clé secrète de signature des jetons (32 caractères minimum). La changer déconnecte tout le monde. |
| `Inscription:CodeInvitation` | code demandé pour créer un compte enseignant. Vide = inscriptions fermées. |
| `Cors:Origines` | adresses du site autorisées à appeler l'API |
| `Site:Dossier` | (facultatif) dossier du site web à servir par l'API elle-même : site et API à la même adresse |
| `Securite:ConnexionsParMinute` | tentatives de connexion par minute et par adresse IP (60 par défaut) |

Pour générer une clé : `[Convert]::ToBase64String((1..48 | % { Get-Random -Max 256 }))` (PowerShell) ou `openssl rand -base64 48`.

### 3. Publication sur IIS (Windows Server)

> Serveur Ubuntu avec Tailscale : tout est automatisé par `deploy/installer.sh`, voir [deploy/README.md](../deploy/README.md).

1. Installer le **.NET 8 Hosting Bundle** sur le serveur, puis redémarrer IIS (`iisreset`).
2. Publier :
   ```bash
   dotnet publish api/MathsSuivi.Api -c Release -o publish
   ```
3. Dans IIS : créer un site (ou une application) pointant vers le dossier `publish`, pool d'applications en **« Aucun code managé »**.
4. Ajouter une **liaison HTTPS** avec un certificat valide (par exemple avec win-acme / Let's Encrypt). Le site élève est en HTTPS : le navigateur refusera d'appeler une API en HTTP.
5. Copier `appsettings.Production.json` dans `publish`.
6. Vérifier : `https://votre-serveur/api/sante` doit répondre `{"statut":"ok",...}`.

L'API fonctionne aussi sous Linux (`dotnet MathsSuivi.Api.dll` derrière Nginx) ou dans un conteneur.

### 4. Relier l'application web

Dans `config.js` à la racine du dépôt :
```js
window.MATHS4E_API = "https://votre-serveur";
```
Pousser sur `main` : Vercel redéploie le site. Les élèves voient alors **Se connecter à ma classe** et l'espace enseignant est disponible sur `https://maths-4e-sigma.vercel.app/prof.html`.

## Développement local

```bash
cd api/MathsSuivi.Api
# appsettings.Development.json : chaîne de connexion, Jwt:Cle, CodeInvitation, Cors:Origines
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

## Points d'accès

| Méthode | Adresse | Qui | Rôle |
|---|---|---|---|
| GET | `/api/sante` | tous | état de l'API et de la base |
| POST | `/api/auth/enseignant/inscription` | tous | créer un compte (code d'invitation) |
| POST | `/api/auth/enseignant/connexion` | tous | connexion enseignant (jeton 12 h) |
| POST | `/api/auth/eleve/connexion` | tous | connexion élève (jeton 60 jours) |
| GET | `/api/progression` | élève | ses résultats |
| POST | `/api/progression` | élève | envoyer des résultats (règles de contrôle appliquées) |
| GET / POST | `/api/classes` | enseignant | lister / créer ses classes |
| DELETE | `/api/classes/{id}` | enseignant | supprimer une classe |
| GET / POST | `/api/classes/{id}/eleves` | enseignant | lister / ajouter des élèves (PIN renvoyés une seule fois) |
| POST | `/api/classes/{id}/eleves/{eleveId}/pin` | enseignant | nouveau code PIN, débloque le compte |
| DELETE | `/api/classes/{id}/eleves/{eleveId}` | enseignant | retirer un élève |
| GET / DELETE | `/api/classes/{id}/eleves/{eleveId}/resultats` | enseignant | détail / effacer les résultats (`?chapitre=` pour un seul chapitre) |
| GET | `/api/classes/{id}/bilan` | enseignant | bilan de la classe : notes /20 par élève et par chapitre |

## Limite connue

La vérification des réponses se fait encore sur la tablette. Le serveur empêche d'effacer ou d'améliorer un résultat déjà enregistré, mais un élève capable d'écrire ses propres requêtes HTTP pourrait envoyer « réussi du premier coup » à une question jamais faite. Pour l'empêcher, il faudrait déplacer la correction des réponses dans l'API.
