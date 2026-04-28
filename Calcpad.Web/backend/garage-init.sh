#!/bin/sh
set -eu

# One-shot Garage bootstrap: assign layout, create bucket, create access key,
# bind key to bucket, and print credentials so the calcpad-server can pick them up.
#
# Idempotent: skips work that's already done.

GARAGE_BIN="/garage"
BUCKET="${CALCPAD_BUCKET:-calcpad}"
KEY_NAME="${CALCPAD_KEY_NAME:-calcpad-server}"
ZONE="${CALCPAD_ZONE:-dc1}"
CAPACITY="${CALCPAD_CAPACITY:-1G}"

echo "[garage-init] waiting for garage RPC..."
for i in $(seq 1 60); do
    if "$GARAGE_BIN" status >/dev/null 2>&1; then break; fi
    sleep 1
done

# Layout: assign the single node if not already assigned.
NODE_ID="$("$GARAGE_BIN" node id -q 2>/dev/null | cut -d@ -f1 || true)"
if [ -n "$NODE_ID" ]; then
    if ! "$GARAGE_BIN" layout show 2>/dev/null | grep -q "$NODE_ID"; then
        echo "[garage-init] assigning layout for node $NODE_ID"
        "$GARAGE_BIN" layout assign -z "$ZONE" -c "$CAPACITY" "$NODE_ID"
        "$GARAGE_BIN" layout apply --version 1
    fi
fi

# Bucket
if ! "$GARAGE_BIN" bucket info "$BUCKET" >/dev/null 2>&1; then
    echo "[garage-init] creating bucket $BUCKET"
    "$GARAGE_BIN" bucket create "$BUCKET"
fi

# Key
if ! "$GARAGE_BIN" key info "$KEY_NAME" >/dev/null 2>&1; then
    echo "[garage-init] creating key $KEY_NAME"
    "$GARAGE_BIN" key create "$KEY_NAME"
fi

"$GARAGE_BIN" bucket allow --read --write --owner "$BUCKET" --key "$KEY_NAME" >/dev/null 2>&1 || true

# Emit credentials to a shared file the server can read.
CREDS_FILE="${CALCPAD_CREDS_FILE:-/shared/garage-creds.env}"
mkdir -p "$(dirname "$CREDS_FILE")"
ACCESS="$("$GARAGE_BIN" key info --show-secret "$KEY_NAME" | awk '/Key ID:/ {print $3}')"
SECRET="$("$GARAGE_BIN" key info --show-secret "$KEY_NAME" | awk '/Secret key:/ {print $3}')"

cat >"$CREDS_FILE" <<EOF
Storage__Enabled=true
Storage__S3__ServiceURL=http://garage:3900
Storage__S3__Region=garage
Storage__S3__BucketName=$BUCKET
Storage__S3__AccessKey=$ACCESS
Storage__S3__SecretKey=$SECRET
Storage__S3__ForcePathStyle=true
Storage__S3__UseHttps=false
EOF

echo "[garage-init] wrote credentials to $CREDS_FILE"
