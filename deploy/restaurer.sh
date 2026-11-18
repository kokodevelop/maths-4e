#!/usr/bin/env bash
# Revient à la version précédente de Maths 4e (api.old et site.old), en cas de mise à jour ratée.
# La base de données n'est pas modifiée.   Usage : sudo ./deploy/restaurer.sh
set -euo pipefail
RACINE=/opt/maths4e
SERVICE=maths4e
[[ $EUID -eq 0 ]] || { echo "Lance le script avec sudo." >&2; exit 1; }
[[ -d "$RACINE/api.old" && -d "$RACINE/site.old" ]] || { echo "Aucune version précédente à restaurer." >&2; exit 1; }

systemctl stop "$SERVICE"
for d in api site; do
  rm -rf "$RACINE/$d.annule"
  mv "$RACINE/$d" "$RACINE/$d.annule"
  mv "$RACINE/$d.old" "$RACINE/$d"
  mv "$RACINE/$d.annule" "$RACINE/$d.old"
done
systemctl start "$SERVICE"
echo "Version précédente restaurée. (La version retirée est dans api.old et site.old.)"
