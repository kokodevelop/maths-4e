# Installer Maths 4e sur un serveur Ubuntu (Tailscale Funnel)

Le site élève, l'espace enseignant et l'API de suivi tournent dans **un seul service** sur le serveur, avec MySQL.
Tailscale Funnel les publie en HTTPS à une adresse du type `https://serveur.ton-tailnet.ts.net` : les élèves n'ont rien à installer à part l'application elle-même.

## Prérequis

- Ubuntu 22.04 ou plus récent, avec `sudo`.
- Tailscale 1.52 ou plus récent, connecté (`tailscale status`), avec **MagicDNS** et **HTTPS** activés dans la console Tailscale.
- **Funnel autorisé** pour ce serveur (console Tailscale → *Access controls* → `nodeAttrs` avec `"attr": ["funnel"]`).
- Le dépôt GitHub cloné sur le serveur (il est privé : clone-le avec ton compte, par exemple avec `gh auth login` ou une clé SSH GitHub).

## Première installation

```bash
git clone https://github.com/kokodevelop/maths-4e.git
cd maths-4e
sudo ./deploy/installer.sh
```

Le script :

1. installe .NET 8 et MySQL (ou utilise MariaDB s'il est déjà là) ;
2. crée `/etc/maths4e/appsettings.Production.json` avec des secrets générés (mot de passe MySQL, clé JWT, code d'invitation enseignant) ;
3. crée la base `maths4e` et son utilisateur ;
4. compile l'API ;
5. installe l'API dans `/opt/maths4e/api` et le site dans `/opt/maths4e/site` ;
6. crée le service systemd `maths4e` (écoute seulement sur `127.0.0.1:8080`) ;
7. publie le service avec Tailscale Funnel et programme une sauvegarde MySQL chaque nuit (14 jours gardés dans `/var/backups/maths4e`).

À la fin, il affiche l'adresse des élèves, celle de l'espace enseignant et le **code d'invitation** pour créer ton compte enseignant sur `/prof.html`.

Si Funnel publie déjà une autre application sur le port 443 du serveur, le script s'arrête sans rien casser : relance-le avec un autre port public (`sudo FUNNEL_PORT=8443 ./deploy/installer.sh`).

## Mettre à jour

```bash
cd maths-4e
git pull
sudo ./deploy/installer.sh
```

La configuration, la base et les comptes sont conservés. La version précédente est gardée dans `/opt/maths4e/api.old` et `site.old`.

## En cas de problème

| Besoin | Commande |
|---|---|
| Voir le journal du service | `journalctl -u maths4e -f` |
| Redémarrer | `sudo systemctl restart maths4e` |
| Revenir à la version précédente | `sudo ./deploy/restaurer.sh` |
| Vérifier l'état | `curl http://127.0.0.1:8080/api/sante` |
| Voir la publication Funnel | `tailscale funnel status` |
| Sauvegarder maintenant | `sudo /usr/local/sbin/maths4e-sauvegarde` |
| Restaurer une sauvegarde | `gunzip -c /var/backups/maths4e/maths4e-AAAA-MM-JJ.sql.gz \| sudo mysql maths4e` |

Pour retrouver le code d'invitation : `sudo grep CodeInvitation /etc/maths4e/appsettings.Production.json`.
