// Direction C v2 — Iteration on Cool Edge
// Changes from v1: no background watermark glyphs, Portugal-PT copy with tu imperatives,
// expandable "Mais detalhes" inside the public card, two night-phase layout variants.

const Cv2 = {
  bg: '#070C12',
  bgRaised: '#0C131D',
  surface: '#101926',
  surfaceHi: '#172335',
  surfaceDeeper: '#0B121C',
  border: 'rgba(63,224,200,0.10)',
  borderStrong: 'rgba(63,224,200,0.28)',
  borderNeutral: 'rgba(255,255,255,0.06)',
  text: '#EAF4FB',
  textDim: '#A4BACE',
  textMuted: '#56697F',
  accent: '#3FE0C8',
  accentBright: '#5FF0D8',
  accentSoft: 'rgba(63,224,200,0.12)',
  accentDim: 'rgba(63,224,200,0.40)',
  werewolf: '#FF6478',
  werewolfSoft: 'rgba(255,100,120,0.12)',
  villager: '#3FE0C8',
  loner: '#B68BFF',
  lonerSoft: 'rgba(182,139,255,0.12)',
  ambiguous: '#FFB35A',
  font: '"Sora", system-ui, sans-serif',
};

// ─── Shared atoms ─────────────────────────────────────────────────────────

function MoonG({ size = 14, color = Cv2.accent }) {
  return (
    <svg width={size} height={size} viewBox="0 0 14 14" fill="none">
      <path d="M11 9.5A5.5 5.5 0 014.5 3a5 5 0 105.5 9.5 5.5 5.5 0 011 -3z"
            stroke={color} strokeWidth="1.3" fill="none" strokeLinejoin="round" />
    </svg>
  );
}

function FangG({ size = 12, color = Cv2.werewolf }) {
  return (
    <svg width={size} height={size} viewBox="0 0 12 12" fill="none">
      <path d="M1 1h10v2L6 11 1 3V1z" fill={color} />
    </svg>
  );
}

function Chevron({ size = 12, open = false, color = Cv2.accent }) {
  return (
    <svg width={size} height={size} viewBox="0 0 12 12" fill="none"
         style={{ transition: 'transform 200ms', transform: open ? 'rotate(180deg)' : 'none' }}>
      <path d="M2.5 4.5L6 8l3.5-3.5" stroke={color} strokeWidth="1.6"
            strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function PhaseHeader({ phase = 'Noite', turn = 4, step = 4, stepCount = 7 }) {
  return (
    <>
      <div style={{
        display: 'flex', alignItems: 'center', gap: 10,
        padding: '18px 22px 12px',
      }}>
        <MoonG size={15} color={Cv2.accent} />
        <span style={{
          fontSize: 12, fontWeight: 700, color: Cv2.text,
          letterSpacing: 1.4, textTransform: 'uppercase',
        }}>{phase}</span>
        <span style={{
          fontSize: 12, fontWeight: 600, color: Cv2.textMuted,
          letterSpacing: 1.4, textTransform: 'uppercase',
        }}>· Turno {String(turn).padStart(2, '0')}</span>
        <span style={{ flex: 1 }} />
        <span style={{
          fontSize: 11, fontWeight: 700, color: Cv2.textMuted,
          letterSpacing: 1, textTransform: 'uppercase',
          fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap',
        }}>Passo {String(step).padStart(2, '0')} / {String(stepCount).padStart(2, '0')}</span>
      </div>
      <div style={{ padding: '0 22px 16px', display: 'flex', gap: 5 }}>
        {Array.from({ length: stepCount }, (_, i) => (
          <div key={i} style={{
            flex: 1, height: 4, borderRadius: 2,
            background: i < step ? Cv2.accent : i === step - 1 ? Cv2.accent : Cv2.surface,
            opacity: i < step ? 1 : 1,
            boxShadow: i === step - 1 ? `0 0 12px ${Cv2.accent}` : 'none',
          }} />
        ))}
      </div>
    </>
  );
}

function ActiveRoleTag({ icon, label }) {
  return (
    <div style={{
      display: 'inline-flex', alignItems: 'center', gap: 10, marginBottom: 14,
    }}>
      <div style={{
        width: 26, height: 26, borderRadius: 7,
        background: Cv2.accentSoft,
        border: `1px solid ${Cv2.borderStrong}`,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
      }}>{icon}</div>
      <span style={{
        fontSize: 11, fontWeight: 700, color: Cv2.accent,
        letterSpacing: 1.4, textTransform: 'uppercase', whiteSpace: 'nowrap',
      }}>{label}</span>
    </div>
  );
}

function WolfG({ size = 14, color = Cv2.accent }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none">
      <path d="M2 5l2-3 1.5 2.5L8 3l2.5 1.5L12 2l2 3v4a6 6 0 01-12 0V5z"
            stroke={color} strokeWidth="1.3" strokeLinejoin="round" fill="none" />
      <circle cx="6" cy="8" r="0.8" fill={color} />
      <circle cx="10" cy="8" r="0.8" fill={color} />
    </svg>
  );
}

// ─── Public card with expandable "Mais detalhes" ───
function PublicCard({ children, initialExpanded = false, expandableText }) {
  const [expanded, setExpanded] = React.useState(initialExpanded);
  const hasExpandable = !!expandableText;
  return (
    <div style={{
      margin: '0 16px 14px',
      padding: '20px 22px 6px',
      background: `linear-gradient(180deg, ${Cv2.surfaceHi} 0%, ${Cv2.surface} 100%)`,
      borderRadius: 14,
      border: `1px solid ${Cv2.borderStrong}`,
    }}>
      <ActiveRoleTag
        icon={<WolfG size={14} color={Cv2.accent} />}
        label="Papel ativo · Pai dos Lobos"
      />
      <div style={{
        fontSize: 11, fontWeight: 700, color: Cv2.textMuted,
        letterSpacing: 1.4, textTransform: 'uppercase', marginBottom: 10,
        display: 'inline-flex', alignItems: 'center', gap: 6,
      }}>
        <span style={{ width: 14, height: 1, background: Cv2.accent }} />
        Em voz alta
      </div>
      {children}

      {/* Expandable — interactive toggle */}
      {hasExpandable && (
        <div style={{
          marginTop: 14, paddingTop: 12,
          borderTop: `1px solid ${Cv2.border}`,
        }}>
          <button
            type="button"
            onClick={() => setExpanded(v => !v)}
            style={{
              display: 'flex', alignItems: 'center', gap: 8,
              width: '100%', padding: '8px 0',
              background: 'transparent', border: 'none', cursor: 'pointer',
              fontFamily: Cv2.font,
              fontSize: 11, fontWeight: 700, color: Cv2.accent,
              letterSpacing: 1.4, textTransform: 'uppercase',
              textAlign: 'left',
            }}
          >
            <Chevron size={11} open={expanded} color={Cv2.accent} />
            {expanded ? 'Menos detalhes' : 'Mais detalhes'}
          </button>
          {expanded && (
            <div style={{
              padding: '8px 0 14px',
              fontSize: 14, lineHeight: 1.5, color: Cv2.textDim, fontWeight: 400,
              textWrap: 'pretty',
            }}>{expandableText}</div>
          )}
        </div>
      )}
    </div>
  );
}

function PrivateCard({ children }) {
  return (
    <div style={{
      margin: '0 16px 14px',
      padding: '16px 20px 18px',
      background: Cv2.surface,
      borderRadius: 12,
      border: `1px solid ${Cv2.borderNeutral}`,
    }}>
      <div style={{
        fontSize: 11, fontWeight: 700, color: Cv2.textMuted,
        letterSpacing: 1.4, textTransform: 'uppercase', marginBottom: 8,
        display: 'flex', alignItems: 'center', gap: 8,
      }}>
        <svg width="11" height="11" viewBox="0 0 11 11" fill="none">
          <rect x="2" y="5" width="7" height="5" rx="1" stroke={Cv2.textMuted} strokeWidth="1.2"/>
          <path d="M3.5 5V3.5a2 2 0 014 0V5" stroke={Cv2.textMuted} strokeWidth="1.2"/>
        </svg>
        Privado · só para ti
      </div>
      <div style={{
        fontSize: 14, lineHeight: 1.5, color: Cv2.textDim, fontWeight: 500,
        textWrap: 'pretty',
      }}>{children}</div>
    </div>
  );
}

function AnnouncementText({ children }) {
  return (
    <div style={{
      fontSize: 22, fontWeight: 700, letterSpacing: -0.5,
      lineHeight: 1.2, color: Cv2.text, textWrap: 'pretty',
    }}>{children}</div>
  );
}

// ─── Player list ───
const ROSTER = [
  { name: 'Ana', seat: 1, alive: true },
  { name: 'Bruno', seat: 2, alive: true },
  { name: 'Catarina', seat: 3, alive: false, died: 'T1' },
  { name: 'Diogo', seat: 4, alive: true },
  { name: 'Eduarda', seat: 5, alive: true },
  { name: 'Francisco', seat: 6, alive: true },
  { name: 'Gonçalo', seat: 7, alive: false, died: 'T2' },
  { name: 'Helena', seat: 8, alive: true },
  { name: 'Inês', seat: 9, alive: true },
  { name: 'João', seat: 10, alive: true },
  { name: 'Luísa', seat: 11, alive: false, died: 'T3' },
  { name: 'Mariana', seat: 12, alive: true, selected: true },
];

function PlayerList({ players = ROSTER, dense = false }) {
  const aliveCount = players.filter(p => p.alive).length;
  return (
    <>
      <div style={{
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        padding: '6px 24px 8px',
      }}>
        <span style={{
          fontSize: 11, fontWeight: 700, color: Cv2.textMuted,
          letterSpacing: 1.4, textTransform: 'uppercase',
        }}>Jogadores · Escolhe a vítima</span>
        <span style={{
          fontSize: 11, fontWeight: 700, color: Cv2.accent,
          letterSpacing: 1.4, textTransform: 'uppercase',
        }}>{String(aliveCount).padStart(2, '0')} vivos</span>
      </div>
      <div style={{ padding: '0 16px' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          {players.map((p) => (
            <div key={p.seat} style={{
              display: 'flex', alignItems: 'center', gap: 14,
              padding: dense ? '10px 16px' : '13px 16px',
              background: p.selected ? Cv2.accentSoft : 'transparent',
              border: p.selected
                ? `1px solid ${Cv2.borderStrong}`
                : `1px solid transparent`,
              borderRadius: 10,
              opacity: p.alive ? 1 : 0.38,
              position: 'relative',
            }}>
              {p.selected && (
                <span style={{
                  position: 'absolute', left: -2, top: '50%', transform: 'translateY(-50%)',
                  width: 3, height: 26, background: Cv2.accent, borderRadius: 2,
                }} />
              )}
              <span style={{
                fontSize: 12, color: p.selected ? Cv2.accent : Cv2.textMuted,
                fontWeight: 700, width: 22, textAlign: 'right',
                fontVariantNumeric: 'tabular-nums', letterSpacing: 0.4,
              }}>{String(p.seat).padStart(2, '0')}</span>
              <span style={{
                flex: 1, fontSize: dense ? 16 : 18, fontWeight: p.selected ? 700 : 500,
                color: p.alive ? Cv2.text : Cv2.textMuted,
                textDecoration: p.alive ? 'none' : 'line-through',
                textDecorationColor: Cv2.textMuted,
                letterSpacing: -0.3,
              }}>{p.name}</span>
              {!p.alive && (
                <span style={{
                  fontSize: 10, color: Cv2.textMuted, fontWeight: 700,
                  letterSpacing: 1, textTransform: 'uppercase',
                }}>{p.died}</span>
              )}
              {p.selected && (
                <div style={{
                  width: 24, height: 24, borderRadius: 6,
                  background: Cv2.accent,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  boxShadow: `0 0 16px ${Cv2.accentDim}`,
                }}>
                  <svg width="12" height="12" viewBox="0 0 12 12" fill="none">
                    <path d="M2 6l3 3 5-6" stroke="#070C12" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                </div>
              )}
            </div>
          ))}
        </div>
      </div>
    </>
  );
}

// ─── Buttons ───
function HoldButton({ label = 'Mantém para confirmar', progress = 0.42 }) {
  return (
    <div style={{
      padding: '12px 16px 28px',
      background: Cv2.bg,
    }}>
      <div style={{
        position: 'relative',
        background: Cv2.surface,
        border: `1.5px solid ${Cv2.accent}`,
        borderRadius: 14,
        height: 60,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        overflow: 'hidden',
        boxShadow: `0 0 40px rgba(63,224,200,0.18)`,
      }}>
        <div style={{
          position: 'absolute', inset: 0, width: `${progress * 100}%`,
          background: `linear-gradient(90deg, ${Cv2.accent} 0%, ${Cv2.accentBright} 100%)`,
        }} />
        <div style={{
          position: 'absolute', left: `${progress * 100}%`, top: 0, bottom: 0, width: 2,
          background: Cv2.accentBright,
          boxShadow: `0 0 12px ${Cv2.accentBright}`,
        }} />
        <span style={{
          position: 'relative', zIndex: 2,
          fontSize: 13, fontWeight: 700, letterSpacing: 0.6,
          color: Cv2.text,
          display: 'flex', alignItems: 'center', gap: 12,
          textTransform: 'uppercase',
        }}>
          <span style={{
            width: 18, height: 18, borderRadius: '50%',
            border: `2px solid ${Cv2.text}`,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
          }}>
            <span style={{ width: 6, height: 6, borderRadius: '50%', background: Cv2.text }} />
          </span>
          {label}
        </span>
      </div>
    </div>
  );
}

function AdvanceButton({ label = 'Toca para avançar' }) {
  return (
    <div style={{ padding: '12px 16px 28px', background: Cv2.bg }}>
      <div style={{
        background: Cv2.accent,
        borderRadius: 14, height: 60,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        gap: 12,
        fontSize: 13, fontWeight: 700, color: '#070C12',
        letterSpacing: 1, textTransform: 'uppercase',
        boxShadow: `0 0 40px rgba(63,224,200,0.32)`,
      }}>
        {label}
        <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
          <path d="M2 7h10m-4-4l4 4-4 4" stroke="#070C12" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </div>
    </div>
  );
}

// ──────────────────────────────────────────────────────────────────────────
// SCREEN 1 — Night Phase, Variant A (combined, expandable EXPANDED)
// ──────────────────────────────────────────────────────────────────────────
function Cv2NightVariantA() {
  return (
    <div style={{
      width: '100%', minHeight: '100%', background: Cv2.bg, color: Cv2.text,
      fontFamily: Cv2.font, display: 'flex', flexDirection: 'column',
      paddingTop: 54, boxSizing: 'border-box',
    }}>
      <PhaseHeader phase="Noite" turn={4} step={4} stepCount={7} />

      <PublicCard
        initialExpanded={true}
        expandableText="O Pai dos Lobos Amaldiçoado tem uma habilidade especial que pode usar uma única vez durante o jogo: em vez de eliminar a vítima, pode optar por infetá-la. A vítima infetada torna-se Lobisomem a partir da noite seguinte, mantendo a sua carta original mas jogando agora do lado dos Lobos. Esta habilidade só pode ser usada uma vez por jogo."
      >
        <AnnouncementText>
          Os Lobisomens acordam. Olhem uns para os outros e reconheçam-se. Escolham em conjunto uma vítima para esta noite, apontando em silêncio.
        </AnnouncementText>
      </PublicCard>

      <PrivateCard>
        O Pai dos Lobos ainda não usou a infeção. Depois de os Lobisomens escolherem a vítima, pergunta discretamente ao Pai dos Lobos se quer eliminar ou infetar. Se optar por infetar, a vítima sobrevive e junta-se aos Lobisomens na próxima noite.
      </PrivateCard>

      <PlayerList dense={true} />

      <div style={{ height: 8 }} />
      <HoldButton progress={0.42} />
    </div>
  );
}

// ──────────────────────────────────────────────────────────────────────────
// SCREEN 2 — Night Phase, Variant B page 1 (public only, expandable COLLAPSED)
// ──────────────────────────────────────────────────────────────────────────
function Cv2NightVariantB1() {
  return (
    <div style={{
      width: '100%', height: '100%', background: Cv2.bg, color: Cv2.text,
      fontFamily: Cv2.font, display: 'flex', flexDirection: 'column',
      paddingTop: 54, boxSizing: 'border-box',
    }}>
      <PhaseHeader phase="Noite" turn={4} step={4} stepCount={7} />

      {/* Page indicator — 1 of 2 */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 10,
        padding: '0 22px 12px',
      }}>
        <span style={{
          fontSize: 10, fontWeight: 700, color: Cv2.textMuted,
          letterSpacing: 1.4, textTransform: 'uppercase',
        }}>Página 1 de 2 · Em voz alta</span>
        <span style={{ flex: 1, height: 1, background: Cv2.borderNeutral }} />
        <span style={{ display: 'flex', gap: 4 }}>
          <span style={{ width: 14, height: 3, background: Cv2.accent, borderRadius: 2 }} />
          <span style={{ width: 14, height: 3, background: Cv2.surface, borderRadius: 2 }} />
        </span>
      </div>

      <PublicCard
        initialExpanded={false}
        expandableText="O Pai dos Lobos Amaldiçoado tem uma habilidade especial que pode usar uma única vez durante o jogo: em vez de eliminar a vítima, pode optar por infetá-la. A vítima infetada torna-se Lobisomem a partir da noite seguinte, mantendo a sua carta original mas jogando agora do lado dos Lobos. Esta habilidade só pode ser usada uma vez por jogo."
      >
        <AnnouncementText>
          Os Lobisomens acordam. Olhem uns para os outros e reconheçam-se. Escolham em conjunto uma vítima para esta noite, apontando em silêncio.
        </AnnouncementText>
      </PublicCard>

      {/* Quiet hint that more comes next */}
      <div style={{
        flex: 1, display: 'flex', flexDirection: 'column',
        alignItems: 'center', justifyContent: 'center',
        padding: '0 28px',
        color: Cv2.textMuted,
      }}>
        <div style={{
          fontSize: 11, fontWeight: 700, letterSpacing: 1.4,
          textTransform: 'uppercase', marginBottom: 10,
        }}>A seguir</div>
        <div style={{
          fontSize: 13, color: Cv2.textDim, textAlign: 'center',
          lineHeight: 1.5, fontWeight: 500,
        }}>
          Instrução privada e seleção da vítima
        </div>
      </div>

      <AdvanceButton label="Toca para avançar" />
    </div>
  );
}

// ──────────────────────────────────────────────────────────────────────────
// SCREEN 3 — Night Phase, Variant B page 2 (private + input + submit)
// ──────────────────────────────────────────────────────────────────────────
function Cv2NightVariantB2() {
  return (
    <div style={{
      width: '100%', height: '100%', background: Cv2.bg, color: Cv2.text,
      fontFamily: Cv2.font, display: 'flex', flexDirection: 'column',
      paddingTop: 54, boxSizing: 'border-box',
    }}>
      <PhaseHeader phase="Noite" turn={4} step={4} stepCount={7} />

      <div style={{
        display: 'flex', alignItems: 'center', gap: 10,
        padding: '0 22px 12px',
      }}>
        <span style={{
          fontSize: 10, fontWeight: 700, color: Cv2.textMuted,
          letterSpacing: 1.4, textTransform: 'uppercase',
        }}>Página 2 de 2 · Ação</span>
        <span style={{ flex: 1, height: 1, background: Cv2.borderNeutral }} />
        <span style={{ display: 'flex', gap: 4 }}>
          <span style={{ width: 14, height: 3, background: Cv2.surface, borderRadius: 2 }} />
          <span style={{ width: 14, height: 3, background: Cv2.accent, borderRadius: 2 }} />
        </span>
      </div>

      {/* Active role mini-tag without the public-card chrome */}
      <div style={{
        margin: '0 22px 12px',
        display: 'flex', alignItems: 'center', gap: 10,
      }}>
        <div style={{
          width: 22, height: 22, borderRadius: 6,
          background: Cv2.accentSoft,
          border: `1px solid ${Cv2.borderStrong}`,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
        }}>
          <WolfG size={11} color={Cv2.accent} />
        </div>
        <span style={{
          fontSize: 11, fontWeight: 700, color: Cv2.accent,
          letterSpacing: 1.4, textTransform: 'uppercase',
        }}>Pai dos Lobos</span>
      </div>

      <PrivateCard>
        O Pai dos Lobos ainda não usou a infeção. Depois de os Lobisomens escolherem a vítima, pergunta discretamente ao Pai dos Lobos se quer eliminar ou infetar. Se optar por infetar, a vítima sobrevive e junta-se aos Lobisomens na próxima noite.
      </PrivateCard>

      <div style={{ flex: 1, overflow: 'auto' }}>
        <PlayerList dense={true} />
        <div style={{ height: 8 }} />
      </div>

      <HoldButton progress={0.42} />
    </div>
  );
}

// ──────────────────────────────────────────────────────────────────────────
// SCREEN 4 — Role Reveal · Helena → Vidente
// ──────────────────────────────────────────────────────────────────────────
function Cv2RoleReveal() {
  const roles = [
    { name: 'Vidente', faction: 'villager', selected: true },
    { name: 'Bruxa', faction: 'villager' },
    { name: 'Caçador', faction: 'villager' },
    { name: 'Cupido', faction: 'villager' },
    { name: 'Defensor', faction: 'villager' },
    { name: 'Pequena Rapariga', faction: 'villager' },
    { name: 'Ladrão', faction: 'loner' },
    { name: 'Ancião', faction: 'villager' },
  ];

  const factionColor = (f) =>
    f === 'werewolf' ? Cv2.werewolf :
    f === 'loner' ? Cv2.loner :
    f === 'ambiguous' ? Cv2.ambiguous : Cv2.villager;
  const factionLabel = (f) =>
    f === 'werewolf' ? 'Lobos' :
    f === 'loner' ? 'Solitário' :
    f === 'ambiguous' ? 'Ambíguo' : 'Aldeia';
  const factionTint = (f) =>
    f === 'werewolf' ? Cv2.werewolfSoft :
    f === 'loner' ? Cv2.lonerSoft : Cv2.accentSoft;

  return (
    <div style={{
      width: '100%', height: '100%', background: Cv2.bg, color: Cv2.text,
      fontFamily: Cv2.font, display: 'flex', flexDirection: 'column',
      paddingTop: 54, boxSizing: 'border-box',
    }}>
      {/* Top label — cause of elimination */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 8,
        padding: '20px 22px 14px',
      }}>
        <svg width="12" height="12" viewBox="0 0 12 12" fill="none">
          <path d="M6 1L2 6h2v4h4V6h2L6 1z" fill={Cv2.werewolf} />
        </svg>
        <span style={{
          fontSize: 11, fontWeight: 700, color: Cv2.werewolf,
          letterSpacing: 1.4, textTransform: 'uppercase',
        }}>Votação da Aldeia · T4</span>
      </div>

      <div style={{ padding: '0 22px 22px' }}>
        <div style={{
          fontSize: 11, fontWeight: 700, color: Cv2.textMuted,
          letterSpacing: 1.4, textTransform: 'uppercase', marginBottom: 6,
        }}>
          Eliminada
        </div>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 14 }}>
          <span style={{
            fontSize: 13, color: Cv2.textMuted, fontWeight: 700,
            letterSpacing: 1, fontVariantNumeric: 'tabular-nums',
          }}>L08</span>
          <span style={{
            fontSize: 46, fontWeight: 700, letterSpacing: -1.6,
            color: Cv2.text, lineHeight: 1,
          }}>Helena</span>
        </div>
      </div>

      {/* Selected role card — no background watermark */}
      <div style={{
        margin: '0 16px 16px',
        padding: '18px 20px',
        background: `linear-gradient(180deg, ${Cv2.surfaceHi} 0%, ${Cv2.surface} 100%)`,
        borderRadius: 14,
        border: `1px solid ${Cv2.borderStrong}`,
      }}>
        <div style={{
          fontSize: 11, fontWeight: 700, color: Cv2.textMuted,
          letterSpacing: 1.4, textTransform: 'uppercase', marginBottom: 10,
        }}>
          A atribuir
        </div>
        <div style={{
          display: 'flex', alignItems: 'center', gap: 14,
        }}>
          <div style={{
            width: 48, height: 48, borderRadius: 12,
            background: Cv2.accent,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            boxShadow: `0 0 28px ${Cv2.accentDim}`,
          }}>
            <span style={{
              fontSize: 22, fontWeight: 800, color: '#070C12',
              letterSpacing: -1,
            }}>V</span>
          </div>
          <div style={{ flex: 1 }}>
            <div style={{
              fontSize: 24, fontWeight: 700, color: Cv2.text,
              letterSpacing: -0.6, lineHeight: 1.1,
            }}>Vidente</div>
            <div style={{
              display: 'flex', alignItems: 'center', gap: 8, marginTop: 5,
            }}>
              <span style={{
                width: 7, height: 7, borderRadius: '50%', background: Cv2.villager,
              }} />
              <span style={{
                fontSize: 11, fontWeight: 700, color: Cv2.villager,
                letterSpacing: 1.4, textTransform: 'uppercase',
              }}>Aldeia</span>
            </div>
          </div>
        </div>
      </div>

      <div style={{
        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
        padding: '6px 24px 8px',
      }}>
        <span style={{
          fontSize: 11, fontWeight: 700, color: Cv2.textMuted,
          letterSpacing: 1.4, textTransform: 'uppercase',
        }}>Papéis disponíveis</span>
        <span style={{
          fontSize: 11, fontWeight: 700, color: Cv2.textMuted,
          letterSpacing: 1, fontVariantNumeric: 'tabular-nums',
        }}>08</span>
      </div>

      <div style={{
        flex: 1, overflow: 'auto', padding: '0 16px',
      }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          {roles.map((r) => (
            <div key={r.name} style={{
              display: 'flex', alignItems: 'center', gap: 14,
              padding: '13px 16px',
              background: r.selected ? Cv2.accentSoft : 'transparent',
              border: r.selected
                ? `1px solid ${Cv2.borderStrong}`
                : `1px solid transparent`,
              borderRadius: 10,
              position: 'relative',
            }}>
              {r.selected && (
                <span style={{
                  position: 'absolute', left: -2, top: '50%', transform: 'translateY(-50%)',
                  width: 3, height: 26, background: Cv2.accent, borderRadius: 2,
                }} />
              )}
              <span style={{
                width: 28, height: 28, borderRadius: 8,
                background: factionTint(r.faction),
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                fontSize: 13, fontWeight: 800,
                color: factionColor(r.faction),
                letterSpacing: -0.5,
              }}>{r.name[0]}</span>
              <span style={{
                flex: 1, fontSize: 17, fontWeight: r.selected ? 700 : 500,
                color: Cv2.text, letterSpacing: -0.2,
              }}>{r.name}</span>
              <span style={{
                fontSize: 10, fontWeight: 700, letterSpacing: 1.2,
                color: factionColor(r.faction), textTransform: 'uppercase',
              }}>{factionLabel(r.faction)}</span>
            </div>
          ))}
        </div>
      </div>

      <div style={{ padding: '14px 16px 28px' }}>
        <div style={{
          background: Cv2.accent,
          borderRadius: 14, height: 60,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          gap: 12,
          fontSize: 14, fontWeight: 700, color: '#070C12',
          letterSpacing: 1, textTransform: 'uppercase',
          boxShadow: `0 0 40px rgba(63,224,200,0.32)`,
        }}>
          Atribuir Vidente
          <svg width="14" height="14" viewBox="0 0 14 14" fill="none">
            <path d="M2 7l3 3 7-7" stroke="#070C12" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </div>
      </div>
    </div>
  );
}

Object.assign(window, {
  Cv2NightVariantA, Cv2NightVariantB1, Cv2NightVariantB2, Cv2RoleReveal,
});
