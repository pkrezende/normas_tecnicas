#!/usr/bin/env bash
# Testa a compatibilidade do cifratoken com "openssl cms" usando certificados de laboratório
# com o mesmo Key Usage do A3 da AC Defesa (digitalSignature, nonRepudiation, keyEncipherment).
# Uso: tests/interop.sh CAMINHO_DO_cifratoken   (ex.: "dotnet run --project src/CifraToken.Cli --")
set -euo pipefail
CLI=${1:?informe o comando do cifratoken}
DIR=$(cd "$(dirname "$0")" && pwd)
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
cd "$T"

lab() {
  openssl req -x509 -newkey rsa:2048 -nodes -keyout "$1.key" -out "$1.pem" -days 30 -subj "/CN=$1 laboratorio" \
    -addext "keyUsage=critical,digitalSignature,nonRepudiation,keyEncipherment" \
    -addext "extendedKeyUsage=clientAuth,emailProtection" 2>/dev/null
  openssl pkcs12 -export -inkey "$1.key" -in "$1.pem" -out "$1.pfx" -passout pass:teste123
}
lab titular; lab contingencia
head -c 300000 /dev/urandom > original.bin
export CIFRATOKEN_SENHA=teste123
ok() { echo "OK  $1"; }
igual() { cmp -s original.bin "$1" || { echo "FALHOU: $1 difere do original"; exit 1; }; }

# 1. cifratoken cifra para dois destinatários; openssl decifra com cada chave
$CLI cifrar -i original.bin -o a.p7m -r titular.pem -r contingencia.pem >/dev/null
openssl cms -decrypt -binary -inform DER -in a.p7m -recip titular.pem -inkey titular.key -out a1.bin; igual a1.bin
openssl cms -decrypt -binary -inform DER -in a.p7m -recip contingencia.pem -inkey contingencia.key -out a2.bin; igual a2.bin
openssl cms -cmsout -print -inform DER -in a.p7m | grep -q "aes-256-cbc" && openssl cms -cmsout -print -inform DER -in a.p7m | grep -q rsaEncryption
ok "cifratoken -> openssl (2 destinatários, AES-256-CBC + rsaEncryption)"

# 2. openssl cifra (comando do parecer); cifratoken decifra com o .pfx
openssl cms -encrypt -binary -aes256 -in original.bin -outform DER -out b.p7m titular.pem
$CLI decifrar -i b.p7m -o b.bin --pfx titular.pfx >/dev/null; igual b.bin
ok "openssl -> cifratoken (DER)"

# 3. mesmo arquivo em PEM
openssl cms -encrypt -binary -aes256 -in original.bin -outform PEM -out c.pem.p7m titular.pem
$CLI decifrar -i c.pem.p7m -o c.bin --pfx titular.pfx >/dev/null; igual c.bin
ok "openssl -> cifratoken (PEM)"

# 4. RSA-OAEP nos dois sentidos
$CLI cifrar -i original.bin -o d.p7m -r titular.pem --oaep >/dev/null
openssl cms -cmsout -print -inform DER -in d.p7m | grep -q rsaesOaep
openssl cms -decrypt -binary -inform DER -in d.p7m -recip titular.pem -inkey titular.key -out d.bin; igual d.bin
openssl cms -encrypt -binary -aes256 -in original.bin -outform DER -out e.p7m -recip titular.pem -keyopt rsa_padding_mode:oaep
$CLI decifrar -i e.p7m -o e.bin --pfx titular.pfx >/dev/null; igual e.bin
ok "RSA-OAEP nos dois sentidos"

# 5. destinatário errado é recusado
if $CLI decifrar -i b.p7m -o x.bin --pfx contingencia.pfx >/dev/null 2>&1; then echo "FALHOU: decifrou com a chave errada"; exit 1; fi
ok "chave de outro certificado é recusada"

# 6. listagem de destinatários
[ "$($CLI destinatarios -i a.p7m | wc -l)" -eq 2 ]
ok "destinatarios lista os 2 certificados"
# 7. cifra com senha no formato .enc do Quimera (Manual v1.0, Apêndice C)
CIFRATOKEN_SENHA='Frase senha de Teste 2026!' $CLI cifrar-senha -i original.bin -o s.enc >/dev/null
openssl enc -aes-256-cbc -d -in s.enc -out s.bin -pbkdf2 -iter 1000000 -md sha512 -pass 'pass:Frase senha de Teste 2026!'; igual s.bin
openssl enc -aes-256-cbc -e -in original.bin -out t.enc -pbkdf2 -iter 1000000 -md sha512 -pass 'pass:Frase senha de Teste 2026!'
CIFRATOKEN_SENHA='Frase senha de Teste 2026!' $CLI decifrar-senha -i t.enc -o t.bin >/dev/null; igual t.bin
if CIFRATOKEN_SENHA=errada $CLI decifrar-senha -i t.enc -o x.bin >/dev/null 2>&1; then echo "FALHOU: aceitou senha errada"; exit 1; fi
ok "senha .enc (openssl enc / Quimera) nos dois sentidos"

# 8. assinatura CAdES com política ICP-Brasil e carimbo do tempo de uma ACT de laboratório
openssl req -x509 -newkey rsa:2048 -nodes -keyout tsa.key -out tsa.pem -days 30 -subj "/CN=ACT de laboratorio" \
  -addext "extendedKeyUsage=critical,timeStamping" -addext "keyUsage=critical,digitalSignature" 2>/dev/null
cat > tsa.cnf <<CNF
[ tsa ]
default_tsa = tsa_config
[ tsa_config ]
serial = $T/tsaserial
default_policy = 1.2.3.4.1
signer_digest = sha256
other_policies = 2.16.76.1.6.15
digests = sha256, sha512
accuracy = secs:1
ordering = no
tsa_name = no
ess_cert_id_chain = no
ess_cert_id_alg = sha256
CNF
echo 01 > tsaserial
PORTA=$((20000 + RANDOM % 20000))
python3 "$DIR/act-local.py" "$T" $PORTA & ACT_PID=$!
trap 'kill $ACT_PID 2>/dev/null; rm -rf "$T"' EXIT
sleep 1
POL="$DIR/CifraToken.Testes/dados/PA_AD_RT_v2_3.der"
CIFRATOKEN_SENHA=teste123 $CLI assinar -i original.bin -o sig.p7s --pfx titular.pfx --politica "$POL" --act "http://127.0.0.1:$PORTA/" >/dev/null
openssl cms -verify -binary -inform DER -in sig.p7s -content original.bin -noverify -out /dev/null 2>/dev/null
openssl cms -cmsout -print -inform DER -in sig.p7s > sig.txt
grep -q "2.16.76.1.7.1.2" sig.txt && grep -q "id-smime-aa-signingCertificateV2" sig.txt && grep -q "id-smime-aa-timeStampToken" sig.txt
$CLI verificar -i original.bin -s sig.p7s | grep -q "Carimbo do tempo:.*íntegro"
cp original.bin alterado.bin; printf 'x' >> alterado.bin
if $CLI verificar -i alterado.bin -s sig.p7s >/dev/null; then echo "FALHOU: assinatura aceitou arquivo alterado"; exit 1; fi
if CIFRATOKEN_SENHA=teste123 $CLI assinar -i original.bin -o x.p7s --pfx titular.pfx --politica "$POL" >/dev/null 2>&1; then echo "FALHOU: AD-RT sem ACT"; exit 1; fi
ok "assinatura CAdES AD-RT com carimbo do tempo (openssl cms -verify, atributos, arquivo alterado recusado)"

# 9. assinatura PAdES dentro do PDF, com estampa de carimbo, conferida pelo pdfsig (poppler)
if command -v pdfsig >/dev/null; then
  python3 - <<'PY'
objs = [b"<</Type/Catalog/Pages 2 0 R>>", b"<</Type/Pages/Kids[3 0 R]/Count 1>>",
        b"<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R>>",
        b"<</Length 44>>stream\nBT /F1 12 Tf 72 720 Td (Documento) Tj ET\nendstream"]
out, offs = bytearray(b"%PDF-1.4\n"), []
for i, o in enumerate(objs, 1):
    offs.append(len(out)); out += b"%d 0 obj\n" % i + o + b"\nendobj\n"
x = len(out)
out += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objs) + 1) + b"".join(b"%010d 00000 n \n" % o for o in offs)
out += b"trailer\n<</Size %d/Root 1 0 R>>\nstartxref\n%d\n%%%%EOF\n" % (len(objs) + 1, x)
open("doc.pdf", "wb").write(out)
PY
  CIFRATOKEN_SENHA=teste123 $CLI assinar-pdf -i doc.pdf -o doc-assinado.pdf --pfx titular.pfx --act "http://127.0.0.1:$PORTA/" --estampa carimbo >/dev/null
  pdfsig doc-assinado.pdf > pdfsig.txt 2>&1 || true
  grep -q "Signature is Valid" pdfsig.txt && grep -q "ETSI.CAdES.detached" pdfsig.txt && grep -q "Total document signed" pdfsig.txt
  $CLI verificar -i doc-assinado.pdf | grep -q "Carimbo do tempo:.*íntegro"
  printf 'x' | dd of=doc-assinado.pdf bs=1 seek=20 conv=notrunc 2>/dev/null
  if $CLI verificar -i doc-assinado.pdf >/dev/null; then echo "FALHOU: PDF alterado aceito"; exit 1; fi
  ok "assinatura PAdES no PDF com carimbo do tempo e estampa (pdfsig: válida, ETSI.CAdES.detached; PDF alterado recusado)"
else
  echo "--  PAdES não testado com pdfsig (falta poppler-utils)"
fi

# 10. token simulado (SoftHSM) por PKCS#11: a chave fica no "token" e não sai dele
SOFTHSM=$(ls /usr/lib/softhsm/libsofthsm2.so /usr/lib/x86_64-linux-gnu/softhsm/libsofthsm2.so 2>/dev/null | head -1 || true)
if [ -n "$SOFTHSM" ] && command -v pkcs11-tool >/dev/null; then
  mkdir tokens
  printf 'directories.tokendir = %s/tokens\nobjectstore.backend = file\n' "$T" > softhsm2.conf
  export SOFTHSM2_CONF=$T/softhsm2.conf
  softhsm2-util --init-token --free --label TOKEN-LAB --so-pin 87654321 --pin 123456 >/dev/null
  openssl pkcs8 -topk8 -nocrypt -in titular.key -outform DER -out titular.p8
  openssl x509 -in titular.pem -outform DER -out titular.der
  pkcs11-tool --module "$SOFTHSM" --login --pin 123456 --write-object titular.p8 --type privkey --id 01 --label titular >/dev/null
  pkcs11-tool --module "$SOFTHSM" --login --pin 123456 --write-object titular.der --type cert --id 01 --label titular >/dev/null
  $CLI listar --pkcs11 "$SOFTHSM" | grep -q "titular laboratorio"
  CIFRATOKEN_PIN=123456 $CLI decifrar -i a.p7m -o f.bin --pkcs11 "$SOFTHSM" >/dev/null; igual f.bin
  CIFRATOKEN_PIN=123456 $CLI decifrar -i e.p7m -o g.bin --pkcs11 "$SOFTHSM" >/dev/null 2>&1; igual g.bin
  $CLI cifrar -i original.bin -o h.p7m --do-token "$SOFTHSM" >/dev/null
  openssl cms -decrypt -binary -inform DER -in h.p7m -recip titular.pem -inkey titular.key -out h.bin; igual h.bin
  if CIFRATOKEN_PIN=000000 $CLI decifrar -i a.p7m -o x.bin --pkcs11 "$SOFTHSM" 2>err.txt >/dev/null; then echo "FALHOU: aceitou PIN errado"; exit 1; fi
  grep -q "PIN incorreto" err.txt
  if CIFRATOKEN_PIN= $CLI decifrar -i a.p7m -o x.bin --pkcs11 "$SOFTHSM" 2>err.txt >/dev/null; then echo "FALHOU: aceitou PIN vazio"; exit 1; fi
  grep -q "Digite o PIN" err.txt
  CIFRATOKEN_PIN=123456 $CLI assinar -i original.bin -o tok.p7s --pkcs11 "$SOFTHSM" --politica "$DIR/CifraToken.Testes/dados/PA_AD_RB_v2_3.der" >/dev/null 2>&1
  openssl cms -verify -binary -inform DER -in tok.p7s -content original.bin -noverify -out /dev/null 2>/dev/null
  ok "token PKCS#11 (SoftHSM): listar, decifrar PKCS#1 e OAEP, cifrar --do-token, assinar, PIN errado"
else
  echo "--  token PKCS#11 não testado (falta softhsm2 ou opensc)"
fi
echo "Todos os testes passaram."
