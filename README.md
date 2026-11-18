# Maths 4e

Application web de cours interactifs de mathématiques pour la classe de 4e (programme du cycle 4).

14 chapitres, chacun en 5 étapes : cours, définitions, QCM de compréhension, exercices intermédiaires et problèmes, avec indices, corrections détaillées et vérification automatique des réponses.

## Utilisation

**En ligne : https://maths-4e-sigma.vercel.app** (hébergé sur Vercel).

En local : ouvrir `index.html` dans un navigateur. Aucune installation ni serveur nécessaire.
La progression est enregistrée dans le navigateur (localStorage).

## Installation sur tablette (PWA)

L'application est une Progressive Web App : une fois hébergée en HTTPS (GitHub Pages, Vercel, IIS…), elle s'installe sur l'écran d'accueil et fonctionne hors connexion.

- **iPad (Safari)** : bouton Partager → « Sur l'écran d'accueil ».
- **Android (Chrome)** : bouton « Installer » sur la page d'accueil, ou menu ⋮ → « Installer l'application ».

Chaque push sur `main` est déployé automatiquement par Vercel. Les tablettes récupèrent la nouvelle version dès qu'elles ouvrent l'application avec du réseau (la page est toujours chargée depuis le réseau en priorité). Augmenter `VERSION` dans `sw.js` seulement si les icônes ou le manifeste changent.

## Suivi des élèves (API VB.NET)

Le dossier `api/` contient l'API de suivi (ASP.NET Core 8, VB.NET, MySQL ou SQL Server) : comptes enseignants, classes, connexion des élèves par code PIN, enregistrement des résultats avec contrôle côté serveur, bilan de classe.
L'espace enseignant est la page `prof.html`.

Installation et configuration : voir [api/README.md](api/README.md). Une fois l'API en ligne, renseigner son adresse dans `config.js`.
