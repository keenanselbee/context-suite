PDF Conversion Improvements Goal
================================

Status: implementation in progress; owner-approved scope, 2026-09-28.

The owner requested these improvements after using installed 1.1.3. Existing
reserved candidates stay unchanged. Installation and publication are separate
from this goal; tested milestones are committed in their respective public and
private repositories, without unrelated work or pushes.

Milestones
----------

1. Use Purchase... and Manage license... consistently, and record owner-confirmed
   live purchase and activation. Portal transfer and refund acceptance remain open.
2. Filter Explorer conversion targets using bounded selection information. A
   PDF-only selection must not offer PDF output; mixed selections retain useful
   applicable targets without parsing documents inside Explorer.
3. Make PDF-to-PNG use white paper by default. Add a secondary transparent-page
   setting, snapshot it for each batch and retain it on retry. Preserve page
   colors, dimensions and ordinary appearance; publish validated copies only.
4. Implement best-effort editable PDF-to-DOCX for text-based documents. Evaluate
   engines, redistribution, resource limits and fidelity before production
   integration. Preserve text, basic formatting, images and tables where practical;
   explain layout limitations and refuse scanned-only inputs requiring OCR.
   OCR is not part of this goal. Preserve sources and validate each DOCX copy.
5. Verify ordinary, multi-column, table/image, Unicode and malformed/protected
   cases, cancellation and publication failures. Record achieved fidelity and
   unsupported variants honestly; engine availability is not acceptance.

The owning product/document design and launch matrix must distinguish proposed
behavior from implemented and verified capabilities throughout this work.
