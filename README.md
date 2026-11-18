# Maths 4e

Application web de cours interactifs de mathématiques pour la classe de 4e (programme du cycle 4).

14 chapitres, chacun en 5 étapes : cours, définitions, QCM de compréhension, exercices intermédiaires et problèmes, avec indices, corrections détaillées et vérification automatique des réponses.

## Utilisation

Ouvrir `index.html` dans un navigateur. Aucune installation ni serveur nécessaire.
La progression est enregistrée dans le navigateur (localStorage).

## Installation sur tablette (PWA)

L'application est une Progressive Web App : une fois hébergée en HTTPS (GitHub Pages, Vercel, IIS…), elle s'installe sur l'écran d'accueil et fonctionne hors connexion.

- **iPad (Safari)** : bouton Partager → « Sur l'écran d'accueil ».
- **Android (Chrome)** : bouton « Installer » sur la page d'accueil, ou menu ⋮ → « Installer l'application ».

Après une modification, augmenter `VERSION` dans `sw.js` pour que les tablettes récupèrent la nouvelle version.
