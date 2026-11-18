#!/usr/bin/env bash
# =============================================================================
#  Maths 4e avec Docker — installation et mises à jour
#  Deux conteneurs (MySQL + application VB.NET qui sert aussi le site),
#  publiés en HTTPS par Tailscale Funnel.
#
#  Depuis le dossier du dépôt cloné :
#      git pull
#      sudo ./deploy/docker.sh
#
#  Les secrets et les ports choisis sont gardés dans le fichier .env (hors Git).
# =============================================================================
set -euo pipefail

DEPOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FICHIER="$DEPOT/.env"
SAUVEGARDES=/var/backups/maths4e

etape()  { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
ok()     { printf '    \033[32m✓\033[0m %s\n' "$*"; }
info()   { printf '    %s\n' "$*"; }
erreur() { printf '\n\033[1;31mErreur : %s\033[0m\n' "$*" >&2; exit 1; }

# ----------------------------------------------------------------- Vérifications
[[ $EUID -eq 0 ]] || erreur "lance le script avec sudo : sudo ./deploy/docker.sh"
[[ -f "$DEPOT/docker-compose.yml" ]] || erreur "lance le script depuis le dépôt maths-4e."
command -v docker >/dev/null || erreur "Docker n'est pas installé."
docker compose version >/dev/null 2>&1 || erreur "le plugin « docker compose » est nécessaire."
command -v tailscale >/dev/null || erreur "Tailscale n'est pas installé sur ce serveur."
command -v python3 >/dev/null || erreur "python3 est nécessaire."
cd "$DEPOT"
compose() { docker compose --env-file "$ENV_FICHIER" "$@"; }

TS_DNS="$(tailscale status --json | python3 -c 'import json,sys; print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))')" \
  || erreur "Tailscale n'est pas connecté (lance « sudo tailscale up »)."
[[ -n "$TS_DNS" ]] || erreur "nom DNS Tailscale introuvable : active MagicDNS et HTTPS dans la console Tailscale."

# Lecture d'une valeur de .env
lire_env() { [[ -f "$ENV_FICHIER" ]] && sed -n "s/^$1=//p" "$ENV_FICHIER" | tail -n1 || true; }
port_pris() {
  if command -v ss >/dev/null; then [[ -n "$(ss -ltnH "sport = :$1")" ]]
  else (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null
  fi
}

# ----------------------------------------------------------------- 1. Secrets et ports
etape "1/5 Configuration (.env)"
PREMIERE=0
if [[ ! -f "$ENV_FICHIER" ]]; then
  PREMIERE=1
  # Port local libre (les autres conteneurs en occupent souvent)
  PORT_LOCAL="${PORT_LOCAL:-}"
  if [[ -z "$PORT_LOCAL" ]]; then
    for p in $(seq 8090 8099) $(seq 18080 18099); do
      if ! port_pris "$p"; then PORT_LOCAL=$p; break; fi
    done
    [[ -n "$PORT_LOCAL" ]] || erreur "aucun port libre trouvé : relance avec sudo PORT_LOCAL=19080 ./deploy/docker.sh"
  fi
  python3 - "$ENV_FICHIER" "$PORT_LOCAL" <<'PY'
import secrets, string, sys
alpha = string.ascii_letters + string.digits
mdp = lambda n: "".join(secrets.choice(alpha) for _ in range(n))
lignes = [
  "# Maths 4e — secrets générés par deploy/docker.sh. Ne pas publier ce fichier.",
  "MYSQL_ROOT_PASSWORD=" + mdp(32),
  "MYSQL_PASSWORD=" + mdp(32),
  "JWT_CLE=" + mdp(64),
  "CODE_INVITATION=" + "".join(secrets.choice("ABCDEFGHJKMNPQRSTUVWXYZ23456789") for _ in range(8)),
  "PORT_LOCAL=" + sys.argv[2],
]
open(sys.argv[1], "w").write("\n".join(lignes) + "\n")
PY
  chmod 600 "$ENV_FICHIER"
  ok "secrets générés dans .env"
else
  ok ".env existant conservé"
fi
PORT_LOCAL="$(lire_env PORT_LOCAL)"
CODE_INVITATION="$(lire_env CODE_INVITATION)"
info "port local : 127.0.0.1:$PORT_LOCAL"

# ----------------------------------------------------------------- 2. Conteneurs
etape "2/5 Construction et démarrage des conteneurs"
if ! compose up -d --build --remove-orphans; then
  echo
  info "Journal de MySQL :"
  compose logs --tail=40 db || true
  if compose logs db 2>/dev/null | grep -q "x86-64-v2"; then
    erreur "le processeur du serveur ne peut pas faire tourner l'image mysql:8.4. Envoie ce message : il faut passer à une image compatible."
  fi
  erreur "un conteneur n'a pas démarré (journal ci-dessus). Relance le script : la première initialisation de MySQL peut être lente."
fi
info "attente du démarrage (MySQL peut prendre une minute la première fois)…"
for _ in $(seq 1 120); do
  if curl -fsS "http://127.0.0.1:$PORT_LOCAL/api/sante" >/dev/null 2>&1; then break; fi
  sleep 2
done
SANTE="$(curl -fsS "http://127.0.0.1:$PORT_LOCAL/api/sante" 2>/dev/null || true)"
if [[ "$SANTE" != *'"ok"'* ]]; then
  compose ps
  compose logs --tail=40 app
  erreur "l'application ne répond pas (journal ci-dessus)."
fi
ok "application active : $SANTE"

# ----------------------------------------------------------------- 3. Base à jour
etape "3/5 Base de données"
# Le script est sans risque à relancer : il ne recrée rien et met à jour la liste des chapitres
compose exec -T db sh -c 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD" 2>/dev/null' < api/sql/01_schema_mysql.sql
NB_ELEVES="$(compose exec -T db sh -c 'mysql -N -uroot -p"$MYSQL_ROOT_PASSWORD" maths4e -e "SELECT COUNT(*) FROM eleve" 2>/dev/null' | tr -d '\r')"
ok "base à jour ($NB_ELEVES élèves)"

# ----------------------------------------------------------------- 4. Tailscale Funnel
etape "4/5 Publication Tailscale Funnel"
TS_SERVE="$(tailscale serve status --json 2>/dev/null || echo '{}')"
# occupant PORT : ce que Funnel publie déjà sur ce port (vide si libre ou si c'est Maths 4e)
occupant() {
  python3 -c '
import json, sys
port, local = sys.argv[1], sys.argv[2]
try: cfg = json.loads(sys.stdin.read() or "{}")
except Exception: sys.exit(0)
nous = {"http://127.0.0.1:" + local, "http://localhost:" + local, "127.0.0.1:" + local, "localhost:" + local, local}
for hote, web in (cfg.get("Web") or {}).items():
    if hote.endswith(":" + port):
        for chemin, h in (web.get("Handlers") or {}).items():
            cible = h.get("Proxy") or h.get("Path") or h.get("Text") or "?"
            if cible.rstrip("/") not in nous:
                print(chemin + " -> " + cible)
fwd = ((cfg.get("TCP") or {}).get(port) or {}).get("TCPForward")
if fwd and fwd not in nous:
    print("TCP -> " + fwd)
' "$1" "$PORT_LOCAL" <<< "$TS_SERVE"
}
FUNNEL_PORT="${FUNNEL_PORT:-$(lire_env FUNNEL_PORT)}"
if [[ -z "$FUNNEL_PORT" ]]; then
  for p in 443 8443 10000; do
    if [[ -z "$(occupant "$p")" ]]; then FUNNEL_PORT=$p; break; fi
  done
  [[ -n "$FUNNEL_PORT" ]] || erreur "les ports Funnel 443, 8443 et 10000 publient déjà d'autres applications :
  443   : $(occupant 443)
  8443  : $(occupant 8443)
  10000 : $(occupant 10000)
  Libère l'un d'eux (sudo tailscale funnel --https=PORT off), puis relance le script."
fi
case "$FUNNEL_PORT" in 443|8443|10000) ;; *) erreur "FUNNEL_PORT doit valoir 443, 8443 ou 10000." ;; esac
AUTRE="$(occupant "$FUNNEL_PORT")"
[[ -z "$AUTRE" ]] || erreur "Funnel publie déjà autre chose sur le port $FUNNEL_PORT ($AUTRE)."
grep -q '^FUNNEL_PORT=' "$ENV_FICHIER" || echo "FUNNEL_PORT=$FUNNEL_PORT" >> "$ENV_FICHIER"
tailscale funnel --bg --yes --https="$FUNNEL_PORT" "http://127.0.0.1:$PORT_LOCAL" >/dev/null 2>&1 \
  || tailscale funnel --bg --https="$FUNNEL_PORT" "http://127.0.0.1:$PORT_LOCAL" >/dev/null \
  || erreur "Funnel n'a pas pu être activé : vérifie que Funnel est autorisé pour ce serveur dans la console Tailscale."
URL="https://$TS_DNS$([[ $FUNNEL_PORT == 443 ]] || echo ":$FUNNEL_PORT")"
ok "publié sur $URL"

# ----------------------------------------------------------------- 5. Sauvegardes
etape "5/5 Sauvegarde quotidienne"
install -d -m 750 "$SAUVEGARDES"
cat > /usr/local/sbin/maths4e-sauvegarde <<SH
#!/usr/bin/env bash
# Sauvegarde quotidienne de la base Maths 4e (conteneur Docker), 14 jours conservés
set -euo pipefail
cd "$DEPOT"
docker compose --env-file "$ENV_FICHIER" exec -T db sh -c 'mysqldump -uroot -p"\$MYSQL_ROOT_PASSWORD" --single-transaction maths4e 2>/dev/null' \\
  | gzip > "$SAUVEGARDES/maths4e-\$(date +%F).sql.gz"
find "$SAUVEGARDES" -name 'maths4e-*.sql.gz' -mtime +14 -delete
SH
chmod 750 /usr/local/sbin/maths4e-sauvegarde
echo "17 2 * * * root /usr/local/sbin/maths4e-sauvegarde" > /etc/cron.d/maths4e-sauvegarde
/usr/local/sbin/maths4e-sauvegarde
ok "sauvegarde chaque nuit à 2 h 17 dans $SAUVEGARDES ($(ls "$SAUVEGARDES" | wc -l) fichier(s))"

# ----------------------------------------------------------------- Résumé
cat <<FIN

$(printf '\033[1;32m')Maths 4e est en ligne.$(printf '\033[0m')

  Élèves (à installer sur les tablettes) : $URL/
  Espace enseignant                      : $URL/prof.html
  Code d'invitation enseignant           : $CODE_INVITATION

FIN
[[ $PREMIERE == 1 ]] && echo "  Crée ton compte enseignant sur prof.html avec ce code (il est aussi dans .env)." && echo
cat <<FIN
  État des conteneurs : sudo docker compose ps
  Journal             : sudo docker compose logs -f app
  Mettre à jour       : git pull && sudo ./deploy/docker.sh
FIN
