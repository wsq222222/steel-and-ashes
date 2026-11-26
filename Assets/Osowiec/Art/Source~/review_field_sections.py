"""Draw two sections from the field geometry used by the Blender export."""
import ast
import math
from pathlib import Path
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

source = Path(__file__).resolve().parent / "make_osowiec_landscape.py"
program = ast.parse(source.read_text(encoding="utf-8-sig"))
survey = {key: getattr(math, key) for key in ("sin", "cos", "sqrt", "exp")}
for node in program.body:
    if isinstance(node, ast.Assign) and isinstance(node.targets[0], ast.Name):
        key = node.targets[0].id
        if key in ("start", "marsh", "tunnel", "craters"):
            survey[key] = ast.literal_eval(node.value)
    if isinstance(node, ast.FunctionDef) and node.name in ("smooth", "pathdist", "n2", "height"):
        exec(compile(ast.Module([node], []), str(source), "exec"), survey)

fig, axes = plt.subplots(2, 1, figsize=(12, 7.6), constrained_layout=True)
fig.set_facecolor("#ede8d8")
fig.suptitle("Осовец · Разрезы игровой локации", x=.07, ha="left", fontsize=22)
for ax, x, extent in zip(axes, (-41, 1), ((-23, -8), (-20, 21))):
    lateral = np.linspace(*extent, 560)
    surface = np.array([survey["height"](x, z) for z in lateral])
    ax.fill_between(lateral, -.4, surface, color="#766f50")
    ax.plot(lateral, surface, color="#3d442b", lw=2)
    ax.set_facecolor("#f5f2e8")
    ax.set_xlim(*extent)
    ax.set_ylim(-.4, 5.9)
    ax.grid(axis="y", linestyle=":", alpha=.35)
    ax.set_axisbelow(True)
    ax.spines[["top","right"]].set_visible(False)
    ax.set_ylabel("Высота, игровые м")
    ax.set_xlabel("Координата Z, игровые м")

trench, fort = axes
trench.set_title("Стартовый настил   /   X = −41", loc="left")
trench.plot([-17.1,-14.9],[.11,.11],color="#965833",lw=5)
trench.scatter([-16],[1.76],color="#9b4f2d",s=32)
trench.annotate("Глаза бойца",(-16,1.76),(-19.3,3.8),arrowprops=dict(arrowstyle="->"))
trench.annotate("Дощатый настил",(-15.1,.11),(-13.6,3.7),arrowprops=dict(arrowstyle="->"))

fort.set_title("Каземат, центральный вал, низина   /   X = 1",loc="left")
theta=np.linspace(0,math.pi,101)
steps=np.array([-6.2,-4.5,-2.6,-1.6,0,1.6,2.6,4.9,6.5])
sod=np.array([.16,1.9,3.63,4.19,4.37,4.18,3.68,1.48,.16])+np.array([.14*survey["n2"](1,z) for z in steps])
fort.plot(-12+steps,sod,color="#65704b",lw=2)
fort.plot(-12+1.72*np.cos(theta),1.83+1.72*np.sin(theta),color="#895a42",lw=5)
fort.vlines([-13.72,-10.28],0,1.83,color="#895a42",lw=5)
fort.annotate("Кирпичный свод",(-12,3.55),(-19,4.9),arrowprops=dict(arrowstyle="->"))
fort.annotate("Гребень вала",(1,survey["height"](1,1)),(4,5.2),arrowprops=dict(arrowstyle="->"))
fort.annotate("Путь через низину",(14,survey["height"](1,14)),(10,3.3),arrowprops=dict(arrowstyle="->"))
dest = Path(sys.argv[1]).resolve()
dest.parent.mkdir(parents=True,exist_ok=True)
fig.savefig(dest,dpi=140,facecolor=fig.get_facecolor())
