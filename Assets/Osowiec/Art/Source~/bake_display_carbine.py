"""Rebuild the carbine alone from its exterior study and painted atlas."""
from pathlib import Path
import sys

src = Path(__file__).resolve().parent
sys.path.insert(0, str(src))
import make_field_cast as cast
import carbine_workshop

cast.clear()
cast.save_export('ServiceCarbine', cast.rifle())
carbine_workshop.export()
