"""Generates samples/tax-return-package.pdf, a fictitious printed tax return package.

The package concatenates several documents the way tax preparation software prints
them into one PDF. It gives the splitter every signal it looks for:

- form titles ("Form 8879", "Form 1040", "SCHEDULE 1", "Form ST-1040NR", ...)
- "Page X of Y" in form headers
- a print-tracking footer line at the bottom of each page
- a landscape page (page size change)
- a blank separator page
- scanned, image-only pages from two different "scanners" (DPI and colour change)

All names, numbers and amounts are invented. SSNs use the 000 area number, which is
never issued, and bank numbers are zeros.

Requirements: pip install reportlab pillow
Usage:        python generate_sample.py [output.pdf]
"""

import io
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.pagesizes import landscape, letter
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

TAXPAYER = "Jordan Q. Sample & Alex R. Sample"
SSN = "000-12-3456"
SPOUSE_SSN = "000-65-4321"
W, H = letter
footer_counter = [100]


def footer(c, width=W):
    """Print-tracking footer: sequence number, then a tracking line."""
    footer_counter[0] += 1
    c.setFont("Helvetica", 7)
    c.drawString(36, 34, str(footer_counter[0]))
    c.drawString(36, 24, f"10000001 200001 SAMPLE01 2025.01000 SAMPLE, JORDAN Q. SAMPLE01")


def header_line(c, left, center, right=""):
    c.setFont("Helvetica-Bold", 16)
    c.drawString(36, H - 60, left)
    c.setFont("Helvetica-Bold", 12)
    c.drawCentredString(W / 2, H - 60, center)
    if right:
        c.setFont("Helvetica", 9)
        c.drawRightString(W - 36, H - 60, right)
    c.line(36, H - 70, W - 36, H - 70)


def lines(c, y, rows, size=10, x=48, step=18):
    c.setFont("Helvetica", size)
    for row in rows:
        if isinstance(row, tuple):
            label, value = row
            c.drawString(x, y, label)
            c.drawRightString(W - 48, y, value)
            c.setDash(1, 2)
            c.line(x + c.stringWidth(label, "Helvetica", size) + 6, y + 2, W - 120, y + 2)
            c.setDash()
        else:
            c.drawString(x, y, row)
        y -= step
    return y


def cover_page(c):
    c.setFont("Helvetica", 9)
    c.drawString(36, H - 40, "Printed copy of an electronically prepared return. Sample document.")
    c.setFont("Helvetica-Bold", 28)
    c.drawCentredString(W / 2, H / 2 + 40, "CLIENT'S COPY")
    c.setFont("Helvetica", 12)
    c.drawCentredString(W / 2, H / 2, "2025 Individual Income Tax Return")
    c.drawCentredString(W / 2, H / 2 - 20, TAXPAYER)
    c.drawCentredString(W / 2, H / 2 - 40, "Prepared by Example Tax Services")
    footer(c)
    c.showPage()


def direct_deposit_report(c):
    states = [("FED", "1040", "4,210."), ("ST", "ST-1040NR", "812."), ("CY", "CY-202", "96.")]
    for page in range(2):
        c.setFont("Helvetica-Bold", 14)
        c.drawRightString(W - 36, H - 50, "Direct Deposit/Debit Report")
        c.setFont("Helvetica", 10)
        c.drawString(36, H - 80, f"Name: {TAXPAYER}")
        c.drawRightString(W - 36, H - 80, f"ID Number: {SSN}")
        y = H - 120
        c.setFont("Helvetica-Bold", 9)
        for x, t in [(36, "Unit"), (80, "Form"), (160, "Financial Institution"), (330, "Account Type"),
                     (410, "Routing Number"), (500, "Amount")]:
            c.drawString(x, y, t)
        c.setFont("Helvetica", 9)
        rows = states if page == 0 else states[:1]
        for unit, form, amount in rows:
            y -= 22
            for x, t in [(36, unit), (80, form), (160, "Example Bank"), (330, "Checking"),
                         (410, "000000000"), (500, amount)]:
                c.drawString(x, y, t)
        footer(c)
        c.showPage()


def form_8879(c):
    for page in (1, 2):
        header_line(c, "Form 8879", "IRS e-file Signature Authorization", f"Page {page} of 2")
        y = H - 100
        if page == 1:
            y = lines(c, y, [
                ("Taxpayer's name", "Jordan Q. Sample"),
                ("Social security number", SSN),
                ("Spouse's name", "Alex R. Sample"),
                ("Spouse's social security number", SPOUSE_SSN),
                "Part I  Tax Return Information - Tax Year Ending December 31, 2025",
                ("1  Adjusted gross income", "98,400."),
                ("2  Total tax", "11,250."),
                ("3  Federal income tax withheld", "7,040."),
                ("4  Amount you want refunded to you", ""),
                ("5  Amount you owe", "4,210."),
            ])
        else:
            y = lines(c, y, [
                "Part II  Taxpayer Declaration and Signature Authorization",
                "Under penalties of perjury, I declare that I have examined a copy of the",
                "electronic individual income tax return and accompanying schedules.",
                ("Your signature", ""),
                ("Date", ""),
                "Part III  Certification and Authentication",
                ("ERO's EFIN/PIN", "00000000000"),
            ])
        footer(c)
        c.showPage()


def form_1040(c):
    header_line(c, "Form 1040", "U.S. Individual Income Tax Return 2025")
    lines(c, H - 100, [
        ("Your first name and middle initial / Last name", "Jordan Q. Sample"),
        ("If joint return, spouse's first name / Last name", "Alex R. Sample"),
        ("Home address", "100 Example Street, Sampletown"),
        "Filing Status:  [x] Married filing jointly",
        ("1a  Total amount from Form(s) W-2, box 1", "92,000."),
        ("2b  Taxable interest", "1,400."),
        ("8   Additional income from Schedule 1, line 10", "5,000."),
        ("9   Total income", "98,400."),
    ])
    footer(c)
    c.showPage()
    header_line(c, "Form 1040 (2025)", "", "Page 2")
    lines(c, H - 100, [
        ("16  Tax", "11,250."),
        ("24  Total tax", "11,250."),
        ("25a Federal income tax withheld from Form(s) W-2", "7,040."),
        ("33  Total payments", "7,040."),
        ("37  Amount you owe", "4,210."),
        "Sign Here: Joint return? See instructions.",
    ])
    footer(c)
    c.showPage()


def schedule_1(c):
    header_line(c, "SCHEDULE 1", "Additional Income and Adjustments to Income", "(Form 1040) 2025")
    lines(c, H - 100, [
        ("Name(s) shown on Form 1040", TAXPAYER),
        "Part I  Additional Income",
        ("3  Business income or (loss)", "5,000."),
        ("10 Combine lines 1 through 9", "5,000."),
        "Part II  Adjustments to Income",
        ("26 Total adjustments", "0."),
    ])
    footer(c)
    c.showPage()


def statement_landscape(c):
    lw, lh = landscape(letter)
    c.setPageSize((lw, lh))
    c.setFont("Helvetica-Bold", 14)
    c.drawString(36, lh - 50, "Statement 1 - Business Income Detail")
    c.setFont("Helvetica", 10)
    c.drawString(36, lh - 70, TAXPAYER)
    y = lh - 110
    cols = [36, 200, 330, 460, 590]
    for i, t in enumerate(["Description", "Gross receipts", "Expenses", "Net", "Notes"]):
        c.setFont("Helvetica-Bold", 10)
        c.drawString(cols[i], y, t)
    c.setFont("Helvetica", 10)
    for row in [("Consulting", "8,200.", "3,400.", "4,800.", "Sample data"),
                ("Workshops", "1,100.", "900.", "200.", "Sample data")]:
        y -= 20
        for i, t in enumerate(row):
            c.drawString(cols[i], y, t)
    footer(c, lw)
    c.showPage()
    c.setPageSize(letter)


def blank_page(c):
    c.showPage()


def scanned_page(text_lines, dpi, mode, quality=80):
    """Renders a raster 'scan' of a letter page, returns (ImageReader, width_pt, height_pt)."""
    width_px, height_px = int(8.5 * dpi), int(11 * dpi)
    img = Image.new("RGB", (width_px, height_px), (250, 250, 247) if mode == "RGB" else (255, 255, 255))
    draw = ImageDraw.Draw(img)
    scale = dpi / 72.0
    try:
        font_big = ImageFont.truetype("DejaVuSans-Bold.ttf", int(18 * scale))
        font = ImageFont.truetype("DejaVuSans.ttf", int(11 * scale))
    except OSError:
        font_big = ImageFont.load_default(size=int(18 * scale))
        font = ImageFont.load_default(size=int(11 * scale))
    y = int(60 * scale)
    for i, line in enumerate(text_lines):
        draw.text((int(54 * scale), y), line, fill=(20, 20, 20), font=font_big if i == 0 else font)
        y += int((34 if i == 0 else 20) * scale)
    if mode == "L":
        img = img.convert("L")
    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=quality, dpi=(dpi, dpi))
    buf.seek(0)
    return ImageReader(buf)


def scanned_documents(c):
    # Two pages from a grayscale 150 DPI scanner: a brokerage statement.
    for page in (1, 2):
        reader = scanned_page([
            "Example Brokerage - Consolidated Statement",
            f"Account holder: {TAXPAYER}",
            "Tax year 2025          Account 000-0000",
            f"Page {page} of 2",
            "",
            "Interest income ............................ 1,400.00" if page == 1 else
            "Realized gains and losses .................. 0.00",
            "Dividends .................................. 0.00",
            "This statement is a sample document.",
        ], dpi=150, mode="L")
        c.drawImage(reader, 0, 0, W, H)
        c.showPage()
    # One page from a colour 200 DPI scanner: a donation receipt.
    reader = scanned_page([
        "Charitable Contribution Receipt",
        "Example Community Foundation",
        f"Received from: {TAXPAYER}",
        "Date: 2025-11-14        Amount: 250.00",
        "No goods or services were provided in exchange.",
        "Sample document.",
    ], dpi=200, mode="RGB")
    c.drawImage(reader, 0, 0, W, H)
    c.showPage()


def estimated_tax_voucher(c):
    header_line(c, "Form 1040-ES", "2026 Estimated Tax Payment Voucher 1")
    lines(c, H - 100, [
        ("Calendar year - Due April 15, 2026", ""),
        ("Amount of estimated tax you are paying by check", "1,000."),
        ("Your first name and middle initial", "Jordan Q."),
        ("Your last name", "Sample"),
        ("Your social security number", SSN),
        ("Address", "100 Example Street, Sampletown"),
    ])
    footer(c)
    c.showPage()


def state_return(c):
    for page in (1, 2):
        header_line(c, "Form ST-1040NR", "State Nonresident Income Tax Return 2025", f"Page {page} of 2")
        rows = [
            ("Name", TAXPAYER),
            ("SSN", SSN),
            ("1  Federal adjusted gross income", "98,400."),
            ("2  Income from state sources", "9,600."),
        ] if page == 1 else [
            ("10 State tax", "812."),
            ("11 Payments", "0."),
            ("12 Balance due", "812."),
            "Declaration: this return is a fictitious sample.",
        ]
        lines(c, H - 100, rows)
        footer(c)
        c.showPage()


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).with_name("tax-return-package.pdf")
    c = canvas.Canvas(str(out), pagesize=letter)
    c.setTitle("Sample tax return package")
    c.setAuthor("Sample generator")
    c.setCreator("generate_sample.py")
    cover_page(c)             # 1
    direct_deposit_report(c)  # 2-3
    form_8879(c)              # 4-5
    form_1040(c)              # 6-7
    schedule_1(c)             # 8
    statement_landscape(c)    # 9 (landscape)
    blank_page(c)             # 10 (separator)
    scanned_documents(c)      # 11-12 (gray 150 DPI), 13 (colour 200 DPI)
    estimated_tax_voucher(c)  # 14
    state_return(c)           # 15-16
    c.save()
    print(f"Wrote {out}")


if __name__ == "__main__":
    main()
