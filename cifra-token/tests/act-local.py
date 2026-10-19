#!/usr/bin/env python3
"""ACT de laboratório (RFC 3161 sobre HTTP) para os testes: responde com "openssl ts -reply".
Uso: act-local.py PASTA PORTA   (PASTA com tsa.cnf, tsa.key e tsa.pem)"""
import http.server, subprocess, sys, os, tempfile

PASTA, PORTA = sys.argv[1], int(sys.argv[2])

class Act(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        pedido = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        with tempfile.TemporaryDirectory() as t:
            q, r = os.path.join(t, "q.tsq"), os.path.join(t, "r.tsr")
            open(q, "wb").write(pedido)
            subprocess.run(["openssl", "ts", "-reply", "-config", os.path.join(PASTA, "tsa.cnf"), "-queryfile", q,
                            "-inkey", os.path.join(PASTA, "tsa.key"), "-signer", os.path.join(PASTA, "tsa.pem"), "-out", r],
                           check=True, capture_output=True)
            resposta = open(r, "rb").read()
        self.send_response(200)
        self.send_header("Content-Type", "application/timestamp-reply")
        self.send_header("Content-Length", str(len(resposta)))
        self.end_headers()
        self.wfile.write(resposta)

    def log_message(self, *a):
        pass

http.server.HTTPServer(("127.0.0.1", PORTA), Act).serve_forever()
