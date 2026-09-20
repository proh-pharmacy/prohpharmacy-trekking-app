#!/bin/bash
if [ -f /app/.env ]; then
  while IFS= read -r line || [ -n "$line" ]; do
    [[ "$line" =~ ^[[:space:]]*# ]] && continue
    [[ -z "$line" ]] && continue
    key="${line%%=*}"
    val="${line#*=}"
    val="${val%\"}"
    val="${val#\"}"
    export "$key=$val"
  done < /app/.env
fi
exec dotnet prohpharmacy_trekking_app.dll
