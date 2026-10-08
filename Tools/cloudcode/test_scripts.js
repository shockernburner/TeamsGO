// Runs the Cloud Code scripts (Assets/_Project/Data/Services/CloudCode) locally against stand-ins for Unity's
// Leaderboards and Cloud Save, to check the rules without touching the live boards. Run: node Tools/cloudcode/test_scripts.js
const Module = require("module");
const path = require("path");
const assert = require("assert");

const boards = {};   // boardId -> { playerId -> { score, metadata } }
const saves = {};    // playerId -> { key -> value }
const fakes = {
  "@unity-services/leaderboards-1.1": {
    LeaderboardsApi: class {
      async getLeaderboardPlayerScore(projectId, boardId, playerId) {
        const e = (boards[boardId] || {})[playerId];
        if (!e) throw new Error("not found");
        return { data: { score: e.score, metadata: JSON.stringify(e.metadata) } };
      }
      async addLeaderboardPlayerScore(projectId, boardId, playerId, body) {
        const b = boards[boardId] = boards[boardId] || {};
        const e = b[playerId] = b[playerId] || { score: 0 };
        e.score += body.score; e.metadata = body.metadata;
        return { data: { score: e.score } };
      }
    },
  },
  "@unity-services/cloud-save-1.4": {
    DataApi: class {
      async getProtectedItems(projectId, playerId, keys) {
        const s = saves[playerId] || {};
        return { data: { results: keys.filter(k => k in s).map(k => ({ key: k, value: s[k] })) } };
      }
      async setProtectedItem(projectId, playerId, item) { (saves[playerId] = saves[playerId] || {})[item.key] = item.value; }
    },
  },
};
const realLoad = Module._load;
Module._load = (req, parent, isMain) => fakes[req] || realLoad(req, parent, isMain);

const dir = path.join(__dirname, "../../Assets/_Project/Data/Services/CloudCode");
const run = require(path.join(dir, "RecordRun.js"));
const team = require(path.join(dir, "RecordCrew.js"));
const logger = { warning() {}, error() {} };
let clock = 1_800_000_000_000;
Date.now = () => clock;
const call = (fn, playerId, params) => fn({ params, context: { projectId: "p", playerId }, logger });

(async () => {
  // A plausible run adds its score to all-time, weekly and the country board.
  let r = await call(run, "alice", { score: 900, seconds: 600, kills: 3, seed: 1, player: "alice", crew: "Ash Line", country: "SG" });
  assert.deepStrictEqual([r.ok, r.delta, r.total], [true, 900, 900]);
  assert.strictEqual(boards.survivors_total_SG.alice.score, 900);
  assert.strictEqual(boards.survivors_week.alice.score, 900);

  // The same island again is refused; so is a second result sooner than a match can be played.
  r = await call(run, "alice", { score: 500, seconds: 600, kills: 1, seed: 1 });
  assert.strictEqual(r.ok, false);
  clock += 60 * 1000;
  r = await call(run, "alice", { score: 500, seconds: 600, kills: 1, seed: 2 });
  assert.strictEqual(r.ok, false);

  // Impossible results are refused.
  clock += 3600 * 1000;
  for (const bad of [{ score: 100, seconds: 10 }, { score: 999999, seconds: 300 }, { score: -5, seconds: 300 }, { score: 10, seconds: 3 * 3600 }]) {
    r = await call(run, "alice", { ...bad, kills: 0, seed: 99 });
    assert.strictEqual(r.ok, false, JSON.stringify(bad));
  }

  // Dying still banks what the player earned (the game's score already costs the death), and a first match
  // with nothing earned still puts the player on the board.
  r = await call(run, "alice", { score: 400, seconds: 400, kills: 0, seed: 3, wiped: true });
  assert.deepStrictEqual([r.ok, r.delta, r.total], [true, 400, 1300]);
  clock += 3600 * 1000;
  r = await call(run, "bob", { score: 0, seconds: 400, kills: 0, seed: 4 });
  assert.deepStrictEqual([r.ok, r.total], [true, 0]);
  assert.strictEqual(boards.survivors_total.bob.score, 0);

  // Test mode writes only to the test_ boards.
  clock += 3600 * 1000;
  r = await call(run, "alice", { score: 300, seconds: 400, kills: 0, seed: 5, test: true });
  assert.strictEqual(boards.test_survivors_total.alice.score, 300);
  assert.strictEqual(boards.survivors_total.alice.score, 1300);

  // Crews: scores times the escape bonus; a wipe takes a quarter of the crew's average match.
  r = await call(team, "alice", { crew: "Ash Line", scores: [400, 300, 200, 100], escaped: 4, seconds: 900, seed: 10 });
  assert.deepStrictEqual([r.ok, r.delta], [true, 2000]);                // 1000 x 2.0
  r = await call(team, "alice", { crew: "Ash Line", scores: [400, 300], escaped: 2, seconds: 900, seed: 11 });
  assert.deepStrictEqual([r.ok, r.delta], [true, 910]);                 // 700 x 1.3
  r = await call(team, "alice", { crew: "Ash Line", scores: [0, 0], escaped: 0, seconds: 900, seed: 12 });
  assert.deepStrictEqual([r.ok, r.delta], [true, -364]);                // average (2910 / 2) x 0.25
  r = await call(team, "alice", { crew: "Ash Line", scores: [10], escaped: 1, seconds: 900, seed: 12 });
  assert.strictEqual(r.ok, false);                                      // same island twice
  assert.strictEqual(boards.teams_total["crew-ash-line"].score, 2546);
  r = await call(team, "alice", { crew: "Ash Line", scores: [999999], escaped: 1, seconds: 100, seed: 13 });
  assert.strictEqual(r.ok, false);                                      // impossible score
  // A new crew whose first match is a wipe is still on the board, at 0.
  r = await call(team, "bob", { crew: "Solo Bob", scores: [250], escaped: 0, seconds: 400, seed: 20 });
  assert.deepStrictEqual([r.ok, r.delta], [true, 0]);
  assert.strictEqual(boards.teams_total["crew-solo-bob"].score, 0);

  console.log("All Cloud Code script checks passed.");
})().catch(e => { console.error("FAILED:", e.message); process.exit(1); });
