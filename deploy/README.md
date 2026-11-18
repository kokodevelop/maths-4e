# Installer Maths 4e sur un serveur (Docker + Tailscale Funnel)

Deux conteneurs : **MySQL 8.4** et l'**application** (API VB.NET .NET 8 qui sert aussi le site élève et l'espace enseignant).
Tailscale Funnel les publie en HTTPS à une adresse du type `https://serveur.ton-tailnet.ts.net:10000` : les élèves n'ont rien d'autre à installer que l'application elle-même.

Rien n'est installé sur le système hôte à part les conteneurs : pas de .NET, pas de MySQL, quelle que soit la version d'Ubuntu.

## Prérequis

- Docker avec le plugin `docker compose`.
- Tailscale 1.52 ou plus récent, connecté, avec **MagicDNS** et **HTTPS** activés dans la console Tailscale.
- **Funnel autorisé** pour ce serveur (console Tailscale → *Access controls* → `nodeAttrs` avec `"attr": ["funnel"]`).
- Le dépôt GitHub cloné sur le serveur (il est privé : clone-le avec ton compte GitHub).

## Installation

```bash
git clone https://github.com/kokodevelop/maths-4e.git ~/maths-4e
cd ~/maths-4e
sudo ./deploy/docker.sh
```

Le script :

1. crée le fichier `.env` (hors Git, lisible par root seulement) avec des secrets générés : mots de passe MySQL, clé des jetons, code d'invitation enseignant, et un **port local libre** (8090 par défaut, les autres conteneurs en occupent souvent) ;
2. construit l'image de l'application et démarre les deux conteneurs (redémarrage automatique avec Docker) ;
3. crée ou met à jour les tables de la base (données dans le volume Docker `maths4e_donnees`) ;
4. publie l'application avec Tailscale Funnel sur le premier port public libre parmi 443, 8443 et 10000 (les seuls autorisés par Funnel) ;
5. programme une sauvegarde de la base chaque nuit (14 jours gardés dans `/var/backups/maths4e`).

À la fin, il affiche l'adresse des élèves, celle de l'espace enseignant (`/prof.html`) et le **code d'invitation** pour créer ton compte enseignant.

L'application n'écoute que sur `127.0.0.1` : elle n'est joignable de l'extérieur que par Funnel.

## Mettre à jour

```bash
cd ~/maths-4e
git pull
sudo ./deploy/docker.sh
```

Les secrets, les ports, les comptes et les résultats sont conservés.

## Commandes utiles

À lancer dans `~/maths-4e` :

| Besoin | Commande |
|---|---|
| État des conteneurs | `sudo docker compose ps` |
| Journal de l'application | `sudo docker compose logs -f app` |
| Redémarrer | `sudo docker compose restart` |
| Arrêter | `sudo docker compose down` (les données restent dans le volume) |
| Vérifier l'état | `curl http://127.0.0.1:8090/api/sante` (port : `PORT_LOCAL` dans `.env`) |
| Voir la publication Funnel | `tailscale funnel status` |
| Sauvegarder maintenant | `sudo /usr/local/sbin/maths4e-sauvegarde` |
| Code d'invitation | `sudo grep CODE_INVITATION .env` |

Restaurer une sauvegarde :

```bash
gunzip -c /var/backups/maths4e/maths4e-AAAA-MM-JJ.sql.gz | sudo docker compose exec -T db sh -c 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD" maths4e'
```

## Sans Docker

`deploy/installer.sh` installe .NET et MySQL directement sur Ubuntu avec un service systemd. Docker reste la méthode recommandée.
