#!/usr/bin/env python3
"""
UMA Connect - Android APK Production Builder & Signer
Builds 100% valid, installable APKs with:
- 4-byte ZIP alignment (zipalign 4)
- APK Signature Scheme v1 (JAR signing with PKCS#7 CERT.RSA)
- APK Signature Scheme v2 (APK Signing Block with 1MB chunked SHA-256 content verification)
- Supports Android 5.0 (API 21) through Android 15 (API 35)
"""

import os
import sys
import struct
import hashlib
import base64
import zlib
import datetime
from cryptography.hazmat.primitives.asymmetric import rsa, padding
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.serialization import Encoding, pkcs7
from cryptography import x509
from cryptography.x509.oid import NameOID
from cryptography.hazmat.backends import default_backend

PROJECT_ROOT = os.path.dirname(os.path.abspath(__file__))
KEY_FILE = os.path.join(PROJECT_ROOT, "uma_connect_key.pem")
CERT_FILE = os.path.join(PROJECT_ROOT, "uma_connect_cert.pem")
APK_BASE_DIR = os.path.join(PROJECT_ROOT, "apk_base")
HTML_FILE = os.path.join(PROJECT_ROOT, "index.html")

TARGET_APKS = [
    os.path.join(PROJECT_ROOT, "UMA Connect.apk"),
    os.path.join(PROJECT_ROOT, "UMA_Connect.apk"),
    os.path.join(PROJECT_ROOT, "UMA_Connect_Fresh.apk")
]

def get_or_create_keypair():
    """Retrieves or creates the permanent production release keypair."""
    if os.path.exists(KEY_FILE) and os.path.exists(CERT_FILE):
        with open(KEY_FILE, "rb") as f:
            key = serialization.load_pem_private_key(f.read(), password=None, backend=default_backend())
        with open(CERT_FILE, "rb") as f:
            cert = x509.load_pem_x509_certificate(f.read(), backend=default_backend())
        return key, cert

    print("[APK Builder] Generating permanent production release keypair...")
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048, backend=default_backend())
    subject = issuer = x509.Name([
        x509.NameAttribute(NameOID.COUNTRY_NAME, "IN"),
        x509.NameAttribute(NameOID.ORGANIZATION_NAME, "UMA Steel Alloys and Power Limited"),
        x509.NameAttribute(NameOID.COMMON_NAME, "UMA Connect"),
    ])
    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(days=1))
        .not_valid_after(datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=365*50))
        .sign(key, hashes.SHA256(), default_backend())
    )

    with open(KEY_FILE, "wb") as f:
        f.write(key.private_bytes(
            Encoding.PEM,
            serialization.PrivateFormat.TraditionalOpenSSL,
            serialization.NoEncryption()
        ))
    with open(CERT_FILE, "wb") as f:
        f.write(cert.public_bytes(Encoding.PEM))

    print(f"[APK Builder] Saved release keypair to {KEY_FILE}")
    return key, cert

def load_payload_files():
    """Loads all payload files to be included in the APK."""
    payload = {}

    # 1. Base files from apk_base
    base_manifest = os.path.join(APK_BASE_DIR, "AndroidManifest.xml")
    base_arsc = os.path.join(APK_BASE_DIR, "resources.arsc")
    base_dex = os.path.join(APK_BASE_DIR, "classes.dex")
    base_icon = os.path.join(APK_BASE_DIR, "res", "mipmap-xxxhdpi-v4", "ic_launcher.png")

    assert os.path.exists(base_manifest), f"Missing {base_manifest}"
    assert os.path.exists(base_arsc), f"Missing {base_arsc}"
    assert os.path.exists(base_dex), f"Missing {base_dex}"
    assert os.path.exists(base_icon), f"Missing {base_icon}"
    assert os.path.exists(HTML_FILE), f"Missing {HTML_FILE}"

    with open(base_manifest, "rb") as f: payload["AndroidManifest.xml"] = f.read()
    with open(base_arsc, "rb") as f:     payload["resources.arsc"] = f.read()
    with open(base_dex, "rb") as f:      payload["classes.dex"] = f.read()
    with open(base_icon, "rb") as f:     payload["res/mipmap-xxxhdpi-v4/ic_launcher.png"] = f.read()

    # 2. Fresh index.html
    with open(HTML_FILE, "rb") as f:
        payload["assets/index.html"] = f.read()

    return payload

def build_aligned_signed_apk(output_path, payload, key, cert):
    """Builds a 4-byte aligned APK with v1 and v2 signatures."""
    cert_der = cert.public_bytes(Encoding.DER)
    pubkey_der = key.public_key().public_bytes(
        Encoding.DER,
        serialization.PublicFormat.SubjectPublicKeyInfo
    )

    entries = dict(payload)

    # 1. Build META-INF/MANIFEST.MF
    manifest_lines = [b"Manifest-Version: 1.0\r\n\r\n"]
    entry_digests = {}
    sorted_names = sorted(entries.keys())

    for name in sorted_names:
        data = entries[name]
        d = base64.b64encode(hashlib.sha256(data).digest()).decode('ascii')
        block = f"Name: {name}\r\nSHA-256-Digest: {d}\r\n\r\n".encode('utf-8')
        manifest_lines.append(block)
        entry_digests[name] = base64.b64encode(hashlib.sha256(block).digest()).decode('ascii')

    manifest_bytes = b"".join(manifest_lines)
    entries["META-INF/MANIFEST.MF"] = manifest_bytes

    # 2. Build META-INF/CERT.SF
    mf_digest = base64.b64encode(hashlib.sha256(manifest_bytes).digest()).decode('ascii')
    sf_lines = [
        b"Signature-Version: 1.0\r\n",
        b"Created-By: 1.0 (Android)\r\n",
        f"SHA-256-Digest-Manifest: {mf_digest}\r\n".encode('ascii'),
        b"X-Android-APK-Signed: 2\r\n\r\n"
    ]
    for name in sorted_names:
        sf_lines.append(f"Name: {name}\r\nSHA-256-Digest: {entry_digests[name]}\r\n\r\n".encode('utf-8'))

    cert_sf_bytes = b"".join(sf_lines)
    entries["META-INF/CERT.SF"] = cert_sf_bytes

    # 3. Build META-INF/CERT.RSA (v1 PKCS#7 signature)
    pkcs7_builder = (
        pkcs7.PKCS7SignatureBuilder()
        .set_data(cert_sf_bytes)
        .add_signer(cert, key, hashes.SHA256())
    )
    cert_rsa_bytes = pkcs7_builder.sign(Encoding.DER, [pkcs7.PKCS7Options.DetachedSignature])
    entries["META-INF/CERT.RSA"] = cert_rsa_bytes

    # 4. Construct ZIP with 4-byte zipalign
    zip_order = [
        "META-INF/MANIFEST.MF",
        "META-INF/CERT.SF",
        "META-INF/CERT.RSA",
        "AndroidManifest.xml",
        "resources.arsc"
    ]
    for name in sorted(entries.keys()):
        if name not in zip_order:
            zip_order.append(name)

    dos_time = (12 << 11) | (0 << 5) | (0 >> 1)
    dos_date = ((2026 - 1980) << 9) | (11 << 5) | 20

    zip_bytes = bytearray()
    cd_entries = []

    for name in zip_order:
        data = entries[name]
        crc = zlib.crc32(data) & 0xFFFFFFFF
        raw_size = len(data)

        # Stored uncompressed for resources.arsc
        if name == "resources.arsc":
            compress_type = 0
            comp_data = data
        else:
            compress_type = 8
            comp = zlib.compressobj(level=9, method=zlib.DEFLATED, wbits=-15)
            comp_data = comp.compress(data) + comp.flush()

        comp_size = len(comp_data)
        local_header_offset = len(zip_bytes)

        # 4-byte alignment calculation
        # Data offset = local_header_offset + 30 + len(name) + len(extra)
        fixed_len = local_header_offset + 30 + len(name.encode('utf-8'))
        padding_len = (4 - (fixed_len % 4)) % 4
        extra = b"\x00" * padding_len

        # Local file header (30 bytes + name + extra)
        lfh = bytearray()
        lfh += b"PK\x03\x04"
        lfh += struct.pack("<H", 20)
        lfh += struct.pack("<H", 0)
        lfh += struct.pack("<H", compress_type)
        lfh += struct.pack("<H", dos_time)
        lfh += struct.pack("<H", dos_date)
        lfh += struct.pack("<I", crc)
        lfh += struct.pack("<I", comp_size)
        lfh += struct.pack("<I", raw_size)
        lfh += struct.pack("<H", len(name.encode('utf-8')))
        lfh += struct.pack("<H", len(extra))
        lfh += name.encode('utf-8')
        lfh += extra

        # Confirm 4-byte alignment
        assert (len(zip_bytes) + len(lfh)) % 4 == 0

        zip_bytes += lfh
        zip_bytes += comp_data

        cd_entries.append({
            "name": name,
            "compress_type": compress_type,
            "crc": crc,
            "comp_size": comp_size,
            "raw_size": raw_size,
            "local_offset": local_header_offset
        })

    cd_start_offset = len(zip_bytes)

    # Central Directory
    cd_bytes = bytearray()
    for e in cd_entries:
        name_bytes = e["name"].encode('utf-8')
        cdh = bytearray()
        cdh += b"PK\x01\x02"
        cdh += struct.pack("<H", 20)
        cdh += struct.pack("<H", 20)
        cdh += struct.pack("<H", 0)
        cdh += struct.pack("<H", e["compress_type"])
        cdh += struct.pack("<H", dos_time)
        cdh += struct.pack("<H", dos_date)
        cdh += struct.pack("<I", e["crc"])
        cdh += struct.pack("<I", e["comp_size"])
        cdh += struct.pack("<I", e["raw_size"])
        cdh += struct.pack("<H", len(name_bytes))
        cdh += struct.pack("<H", 0) # Extra len
        cdh += struct.pack("<H", 0) # Comment len
        cdh += struct.pack("<H", 0) # Disk start
        cdh += struct.pack("<H", 0) # Internal attrs
        cdh += struct.pack("<I", 0) # External attrs
        cdh += struct.pack("<I", e["local_offset"])
        cdh += name_bytes
        cd_bytes += cdh

    cd_size = len(cd_bytes)

    # EOCD
    eocd_bytes = bytearray()
    eocd_bytes += b"PK\x05\x06"
    eocd_bytes += struct.pack("<H", 0)
    eocd_bytes += struct.pack("<H", 0)
    eocd_bytes += struct.pack("<H", len(cd_entries))
    eocd_bytes += struct.pack("<H", len(cd_entries))
    eocd_bytes += struct.pack("<I", cd_size)
    eocd_bytes += struct.pack("<I", cd_start_offset)
    eocd_bytes += struct.pack("<H", 0)

    # 5. Compute APK Signature Scheme v2 (1MB chunked digests)
    sec1 = bytes(zip_bytes)
    sec2 = bytes(cd_bytes)
    sec3 = bytes(eocd_bytes)

    CHUNK_SIZE = 1048576
    chunk_digests = []
    for section in [sec1, sec2, sec3]:
        for i in range(0, len(section), CHUNK_SIZE):
            chunk = section[i:i+CHUNK_SIZE]
            h = hashlib.sha256(b"\xa5" + struct.pack("<I", len(chunk)) + chunk).digest()
            chunk_digests.append(h)

    total_chunks = len(chunk_digests)
    top_digest = hashlib.sha256(b"\x5a" + struct.pack("<I", total_chunks) + b"".join(chunk_digests)).digest()

    # Build v2 Signer
    algo_id = 0x0103 # SIGNATURE_RSA_PKCS1_V1_5_WITH_SHA256
    digest_entry = struct.pack("<I", algo_id) + struct.pack("<I", len(top_digest)) + top_digest
    digests_seq = struct.pack("<I", len(digest_entry)) + digest_entry
    digests_field = struct.pack("<I", len(digests_seq)) + digests_seq

    cert_entry = struct.pack("<I", len(cert_der)) + cert_der
    certs_seq = struct.pack("<I", len(cert_entry)) + cert_entry
    certs_field = struct.pack("<I", len(certs_seq)) + certs_seq

    attrs_field = struct.pack("<I", 0)

    signed_data = digests_field + certs_field + attrs_field
    signed_data_field = struct.pack("<I", len(signed_data)) + signed_data

    sig_bytes = key.sign(signed_data, padding.PKCS1v15(), hashes.SHA256())
    sig_entry = struct.pack("<I", algo_id) + struct.pack("<I", len(sig_bytes)) + sig_bytes
    sigs_seq = struct.pack("<I", len(sig_entry)) + sig_entry
    sigs_field = struct.pack("<I", len(sigs_seq)) + sigs_seq

    pubkey_field = struct.pack("<I", len(pubkey_der)) + pubkey_der

    signer = signed_data_field + sigs_field + pubkey_field
    signer_field = struct.pack("<I", len(signer)) + signer
    signers_seq = struct.pack("<I", len(signer_field)) + signer_field

    # APK Signature Scheme v2 Block (ID = 0x7109871a)
    v2_id = 0x7109871a
    v2_pair = struct.pack("<Q", 4 + len(signers_seq)) + struct.pack("<I", v2_id) + signers_seq

    # APK Signing Block
    block_size = len(v2_pair) + 24
    signing_block = struct.pack("<Q", block_size) + v2_pair + struct.pack("<Q", block_size) + b"APK Sig Block 42"

    # Assemble Final APK
    new_cd_offset = len(sec1) + len(signing_block)
    final_eocd = bytearray(sec3)
    struct.pack_into("<I", final_eocd, 16, new_cd_offset)

    final_apk = sec1 + signing_block + sec2 + bytes(final_eocd)

    with open(output_path, "wb") as f:
        f.write(final_apk)

    return len(final_apk)

def verify_apk(apk_path):
    """Verifies that an APK adheres strictly to Android signature and alignment rules."""
    with open(apk_path, "rb") as f:
        apk_bytes = f.read()

    eocd_pos = apk_bytes.rfind(b"PK\x05\x06")
    assert eocd_pos != -1, "No EOCD found"
    disk_num, cd_disk, cd_entries_disk, cd_entries_total, cd_size, cd_offset, comment_len = struct.unpack(
        "<HHHHIIH", apk_bytes[eocd_pos+4:eocd_pos+22]
    )

    # 1. Verify v2 signing block
    magic = apk_bytes[cd_offset-16 : cd_offset]
    assert magic == b"APK Sig Block 42", "Missing APK Sig Block magic"
    block_size_end = struct.unpack("<Q", apk_bytes[cd_offset-24 : cd_offset-16])[0]
    block_start = cd_offset - 8 - block_size_end
    block_size_start = struct.unpack("<Q", apk_bytes[block_start : block_start+8])[0]
    assert block_size_start == block_size_end, "Signing block size mismatch"

    # 2. Verify v2 content digests
    sec1 = apk_bytes[:block_start]
    sec2 = apk_bytes[cd_offset:eocd_pos]
    sec3_mod = bytearray(apk_bytes[eocd_pos:])
    struct.pack_into("<I", sec3_mod, 16, block_start)

    CHUNK_SIZE = 1048576
    chunk_digests = []
    for section in [sec1, sec2, bytes(sec3_mod)]:
        for i in range(0, len(section), CHUNK_SIZE):
            chunk = section[i:i+CHUNK_SIZE]
            h = hashlib.sha256(b"\xa5" + struct.pack("<I", len(chunk)) + chunk).digest()
            chunk_digests.append(h)

    computed_top_digest = hashlib.sha256(b"\x5a" + struct.pack("<I", len(chunk_digests)) + b"".join(chunk_digests)).digest()

    pairs_data = apk_bytes[block_start+8 : cd_offset-24]
    v2_payload = None
    pos = 0
    while pos < len(pairs_data):
        pair_len, pair_id = struct.unpack("<QI", pairs_data[pos:pos+12])
        pair_val = pairs_data[pos+12 : pos+8+pair_len]
        if pair_id == 0x7109871a:
            v2_payload = pair_val
        pos += 8 + pair_len
    assert v2_payload is not None, "v2 block missing"

    seq_len = struct.unpack("<I", v2_payload[:4])[0]
    signer_data = v2_payload[4:4+seq_len]
    signer_len = struct.unpack("<I", signer_data[:4])[0]
    signer = signer_data[4:4+signer_len]

    sd_len = struct.unpack("<I", signer[:4])[0]
    signed_data = signer[4:4+sd_len]

    spos = 4 + sd_len
    sigs_seq_len = struct.unpack("<I", signer[spos:spos+4])[0]
    sigs_data = signer[spos+4 : spos+4+sigs_seq_len]
    spos += 4 + sigs_seq_len
    pubkey_len = struct.unpack("<I", signer[spos:spos+4])[0]
    pubkey_bytes = signer[spos+4 : spos+4+pubkey_len]

    pubkey = serialization.load_der_public_key(pubkey_bytes, default_backend())

    sig_len = struct.unpack("<I", sigs_data[:4])[0]
    sig_entry = sigs_data[4:4+sig_len]
    algo_id, raw_sig_len = struct.unpack("<II", sig_entry[:8])
    sig_bytes = sig_entry[8:8+raw_sig_len]

    # Verify signature
    pubkey.verify(sig_bytes, signed_data, padding.PKCS1v15(), hashes.SHA256())

    # Verify digest inside signed_data
    df_len = struct.unpack("<I", signed_data[:4])[0]
    digests_seq = signed_data[4 : 4+df_len]
    d_entry_len = struct.unpack("<I", digests_seq[:4])[0]
    d_entry = digests_seq[4:4+d_entry_len]
    d_algo, d_len = struct.unpack("<II", d_entry[:8])
    stored_top_digest = d_entry[8:8+d_len]
    assert computed_top_digest == stored_top_digest, "v2 content digest mismatch"

    # 3. Verify ZIP alignment
    import zipfile
    with zipfile.ZipFile(apk_path, "r") as z:
        for info in z.infolist():
            lh = apk_bytes[info.header_offset : info.header_offset+30]
            fname_len = struct.unpack("<H", lh[26:28])[0]
            extra_len = struct.unpack("<H", lh[28:30])[0]
            data_offset = info.header_offset + 30 + fname_len + extra_len
            if info.compress_type == 0:
                assert data_offset % 4 == 0, f"Uncompressed file {info.filename} not 4-byte aligned"

    return True

def main():
    print("=================================================================")
    print("   UMA Connect - Android APK Production Builder & Signer")
    print("=================================================================")
    key, cert = get_or_create_keypair()
    payload = load_payload_files()
    print(f"[APK Builder] Loaded {len(payload)} payload items (HTML: {len(payload['assets/index.html'])} bytes)")

    for target in TARGET_APKS:
        size = build_aligned_signed_apk(target, payload, key, cert)
        verify_apk(target)
        print(f"[APK Builder] [PASS] {os.path.basename(target)} ({size:,} bytes) - v1 + v2 Signed, 4-Byte Aligned")

    print("\n[APK Builder] ALL APKS SUCCESSFULLY GENERATED AND VERIFIED!")
    print("  Compatible: Android 5.0 (API 21) through Android 15 (API 35)")
    print("=================================================================\n")

if __name__ == "__main__":
    main()
