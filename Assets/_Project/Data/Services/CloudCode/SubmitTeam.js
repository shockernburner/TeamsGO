// TETHER: Primal — a crew's finished online match, sent by its host, checked and added to the crew's totals.
// The same rules as Match/CareerScore.TeamDelta: the crew's scores times a bonus for how many got out
// (1: x1.0, 2: x1.3, 3: x1.6, 4: x2.0); a wipe (nobody out) takes a quarter of the crew's average match.
// A crew's entry on the teams boards is keyed by its name, so the whole crew shares one total.
const { LeaderboardsApi } = require("@unity-services/leaderboards-1.1");

const MIN_SECONDS = 30, MAX_SECONDS = 45 * 60, MAX_POINTS_PER_SECOND = 60, WIPE_FRACTION = 0.25;

function bonus(escaped) { return escaped >= 4 ? 2.0 : escaped === 3 ? 1.6 : escaped === 2 ? 1.3 : escaped === 1 ? 1.0 : 0; }
function teamKey(crew) {
  const slug = crew.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 40);
  return slug ? "crew-" + slug : null;
}

module.exports = async ({ params, context, logger }) => {
  const crew = String(params.crew || "").trim().slice(0, 24);
  const scores = Array.isArray(params.scores) ? params.scores.map(s => Math.max(0, Math.floor(Number(s) || 0))).slice(0, 4) : [];
  const escaped = Math.max(0, Math.min(scores.length, Math.floor(Number(params.escaped) || 0)));
  const seconds = Number(params.seconds) || 0, seed = Math.floor(Number(params.seed) || 0), test = params.test === true;
  const key = teamKey(crew);
  if (!key || scores.length === 0) return { ok: false, reason: "no crew" };
  if (!(seconds >= MIN_SECONDS) || seconds > MAX_SECONDS) return { ok: false, reason: "match length" };
  for (const s of scores) if (s > seconds * MAX_POINTS_PER_SECOND) return { ok: false, reason: "score too high for the time played" };

  const boards = new LeaderboardsApi(context);   // server access: the crew's entry isn't the caller's own
  const pre = test ? "test_" : "";
  let total = 0, matches = 0, lastSeed = null;
  try {
    const e = (await boards.getLeaderboardPlayerScore(context.projectId, pre + "teams_total", key)).data;
    total = e.score || 0;
    const m = typeof e.metadata === "string" ? JSON.parse(e.metadata) : (e.metadata || {});
    matches = m.matches || 0; lastSeed = m.seed;
  } catch (e) { /* a new crew */ }
  if (lastSeed === seed) return { ok: false, reason: "this island's result is already in" };

  const average = matches > 0 ? total / matches : 0;
  let delta = escaped > 0 ? Math.round(scores.reduce((a, b) => a + b, 0) * bonus(escaped)) : -Math.round(average * WIPE_FRACTION);
  delta = Math.max(delta, -total);
  const meta = { crew: crew, matches: matches + 1, seed: seed, size: scores.length };
  for (const board of [pre + "teams_total", pre + "teams_week"]) {
    if (delta === 0) continue;
    await boards.addLeaderboardPlayerScore(context.projectId, board, key, { score: delta, metadata: meta });
  }
  return { ok: true, delta: delta, total: total + delta };
};

module.exports.params = {
  crew: "String", scores: "JSON", escaped: "Numeric", seconds: "Numeric", seed: "Numeric", test: "Boolean"
};
