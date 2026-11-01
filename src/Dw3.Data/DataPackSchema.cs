using Microsoft.Data.Sqlite;

namespace Dw3.Data;

/// <summary>
/// Defines the versioned SQLite structures shared by the extractor and game runtime.
/// </summary>
public static class DataPackSchema
{
    /// <summary>
    /// Gets the newest pack schema that this runtime can read.
    /// </summary>
    public const int Version = 16;

    /// <summary>
    /// Creates the schema structures required by every data pack.
    /// </summary>
    /// <param name="connection">An open SQLite connection to the data-pack database.</param>
    public static void Create(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE meta (
                schema_version INTEGER NOT NULL,
                extractor_version TEXT NOT NULL,
                disc_sha1 TEXT NOT NULL,
                edition TEXT NOT NULL,
                completed_at TEXT NOT NULL
            );
            CREATE TABLE pack_images (
                relative_path TEXT PRIMARY KEY NOT NULL
            );
            CREATE TABLE zones (
                stage_id INTEGER PRIMARY KEY NOT NULL,
                tile_columns INTEGER NOT NULL,
                tile_rows INTEGER NOT NULL,
                width_pixels INTEGER NOT NULL,
                height_pixels INTEGER NOT NULL
            );
            CREATE TABLE field_stages (
                stage_id INTEGER PRIMARY KEY NOT NULL,
                mode INTEGER UNIQUE NOT NULL,
                overlay_file_id INTEGER NOT NULL,
                status TEXT NOT NULL,
                error TEXT NULL
            );
            CREATE TABLE field_warps (
                source_stage_id INTEGER NOT NULL,
                slot_index INTEGER NOT NULL,
                destination_mode INTEGER NOT NULL,
                destination_stage_id INTEGER NULL,
                x INTEGER NOT NULL,
                y INTEGER NOT NULL,
                facing INTEGER NOT NULL,
                condition INTEGER NOT NULL,
                condition_status TEXT NOT NULL,
                PRIMARY KEY (source_stage_id, slot_index),
                FOREIGN KEY (source_stage_id) REFERENCES field_stages(stage_id),
                FOREIGN KEY (destination_stage_id) REFERENCES field_stages(stage_id)
            );
            CREATE TABLE water_zones (
                stage_id INTEGER PRIMARY KEY NOT NULL,
                overlay_sha256 TEXT NOT NULL,
                sheet_sha256 TEXT NOT NULL,
                texture_sha256 TEXT NOT NULL,
                archive_entries INTEGER NOT NULL
            );
            CREATE TABLE water_skipped_zones (
                stage_id INTEGER PRIMARY KEY NOT NULL,
                error TEXT NOT NULL
            );
            CREATE TABLE water_objects (
                stage_id INTEGER NOT NULL,
                object_index INTEGER NOT NULL,
                visible INTEGER NOT NULL,
                anim INTEGER NOT NULL,
                margin INTEGER NOT NULL,
                depth INTEGER NOT NULL,
                frame INTEGER NOT NULL,
                cycle INTEGER NOT NULL,
                cycle_first INTEGER NOT NULL,
                cycle_last INTEGER NOT NULL,
                cycle_delay INTEGER NOT NULL,
                clut_row INTEGER NOT NULL,
                x INTEGER NOT NULL,
                y INTEGER NOT NULL,
                sort_y INTEGER NOT NULL,
                cycle_time INTEGER NOT NULL,
                source_address INTEGER NOT NULL,
                PRIMARY KEY (stage_id, object_index),
                FOREIGN KEY (stage_id) REFERENCES water_zones(stage_id)
            );
            CREATE TABLE water_object_frames (
                stage_id INTEGER NOT NULL,
                object_index INTEGER NOT NULL,
                ordinal INTEGER NOT NULL,
                frame_id INTEGER NOT NULL,
                clut_row INTEGER NOT NULL,
                offset_x INTEGER NOT NULL,
                offset_y INTEGER NOT NULL,
                abr INTEGER NOT NULL,
                opaque_image TEXT NOT NULL,
                stp_image TEXT NOT NULL,
                PRIMARY KEY (stage_id, object_index, ordinal),
                FOREIGN KEY (stage_id, object_index) REFERENCES water_objects(stage_id, object_index)
            );
            CREATE TABLE fieldcom_domains (domain TEXT PRIMARY KEY NOT NULL, disc_path TEXT NOT NULL, sha256 TEXT NOT NULL, bytes INTEGER NOT NULL);
            CREATE TABLE fieldcom_frames (domain TEXT NOT NULL, frame_id INTEGER NOT NULL, width INTEGER NOT NULL, height INTEGER NOT NULL, offset_x INTEGER NOT NULL, offset_y INTEGER NOT NULL, row INTEGER NOT NULL, abr INTEGER NOT NULL, image_path TEXT NOT NULL, PRIMARY KEY(domain, frame_id));
            CREATE TABLE cue_exit_rows (direction INTEGER PRIMARY KEY NOT NULL, row INTEGER NOT NULL);
            CREATE TABLE cue_sequences (kind TEXT NOT NULL, name TEXT NOT NULL, ordinal INTEGER NOT NULL, frame_id INTEGER NOT NULL, duration INTEGER NOT NULL, field_row INTEGER NULL, PRIMARY KEY(kind,name,ordinal));
            CREATE TABLE icon_animations (animation INTEGER PRIMARY KEY NOT NULL, loop_target INTEGER NOT NULL);
            CREATE TABLE icon_animation_steps (animation INTEGER NOT NULL, ordinal INTEGER NOT NULL, frame_id INTEGER NOT NULL, duration INTEGER NOT NULL, PRIMARY KEY(animation,ordinal));
            CREATE TABLE action_domains (
                domain TEXT PRIMARY KEY NOT NULL,
                file_id INTEGER NOT NULL,
                anim_set_entry INTEGER NOT NULL,
                manifest_schema TEXT NOT NULL,
                disc_path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                bytes INTEGER NOT NULL
            );
            CREATE TABLE action_frames (
                domain TEXT NOT NULL,
                frame_id INTEGER NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                image_path TEXT NOT NULL,
                PRIMARY KEY (domain, frame_id),
                FOREIGN KEY (domain) REFERENCES action_domains(domain)
            );
            CREATE TABLE action_sets (
                domain TEXT NOT NULL,
                set_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                terminator TEXT NOT NULL,
                PRIMARY KEY (domain, set_id),
                FOREIGN KEY (domain) REFERENCES action_domains(domain)
            );
            CREATE TABLE action_records (
                domain TEXT NOT NULL,
                set_id INTEGER NOT NULL,
                ordinal INTEGER NOT NULL,
                duration INTEGER NOT NULL,
                frame_id INTEGER NOT NULL,
                offset_x INTEGER NOT NULL,
                offset_y INTEGER NOT NULL,
                PRIMARY KEY (domain, set_id, ordinal),
                FOREIGN KEY (domain, set_id) REFERENCES action_sets(domain, set_id)
            );
            CREATE TABLE npc_actors (
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                actor_id INTEGER NOT NULL,
                image INTEGER NOT NULL,
                x INTEGER NOT NULL,
                y INTEGER NOT NULL,
                direction INTEGER NOT NULL,
                width INTEGER NULL,
                is_player_lead INTEGER NOT NULL,
                snapshot_visible INTEGER NOT NULL,
                file_number INTEGER NULL,
                archive_entry INTEGER NULL,
                archive_disc_path TEXT NULL,
                PRIMARY KEY (stage_id, actor_index)
            );
            CREATE TABLE npc_actor_conditions (
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                ordinal INTEGER NOT NULL,
                code INTEGER NOT NULL,
                value INTEGER NOT NULL,
                PRIMARY KEY (stage_id, actor_index, ordinal),
                FOREIGN KEY (stage_id, actor_index) REFERENCES npc_actors(stage_id, actor_index)
            );
            CREATE TABLE npc_characters (
                actor_id INTEGER NOT NULL,
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                manifest_schema TEXT NOT NULL,
                archive_disc_path TEXT NOT NULL,
                archive_sha256 TEXT NOT NULL,
                archive_bytes INTEGER NOT NULL,
                archive_entry INTEGER NOT NULL,
                frame_count INTEGER NOT NULL,
                PRIMARY KEY (actor_id, stage_id, actor_index)
            );
            CREATE TABLE npc_anim_sets (
                actor_id INTEGER NOT NULL,
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                set_id INTEGER NOT NULL,
                seq0 INTEGER NOT NULL,
                seq1 INTEGER NOT NULL,
                seq2 INTEGER NOT NULL,
                seq3 INTEGER NOT NULL,
                seq4 INTEGER NOT NULL,
                PRIMARY KEY (actor_id, stage_id, actor_index, set_id),
                FOREIGN KEY (actor_id, stage_id, actor_index)
                    REFERENCES npc_characters(actor_id, stage_id, actor_index)
            );
            CREATE TABLE npc_sequences (
                actor_id INTEGER NOT NULL,
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                seq_index INTEGER NOT NULL,
                terminator TEXT NOT NULL,
                PRIMARY KEY (actor_id, stage_id, actor_index, seq_index),
                FOREIGN KEY (actor_id, stage_id, actor_index)
                    REFERENCES npc_characters(actor_id, stage_id, actor_index)
            );
            CREATE TABLE npc_sequence_records (
                actor_id INTEGER NOT NULL,
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                seq_index INTEGER NOT NULL,
                ordinal INTEGER NOT NULL,
                duration INTEGER NOT NULL,
                frame_id INTEGER NOT NULL,
                offset_x INTEGER NOT NULL,
                offset_y INTEGER NOT NULL,
                PRIMARY KEY (actor_id, stage_id, actor_index, seq_index, ordinal),
                FOREIGN KEY (actor_id, stage_id, actor_index, seq_index)
                    REFERENCES npc_sequences(actor_id, stage_id, actor_index, seq_index)
            );
            CREATE TABLE npc_frames (
                actor_id INTEGER NOT NULL,
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                frame_id INTEGER NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                image_path TEXT NOT NULL,
                PRIMARY KEY (actor_id, stage_id, actor_index, frame_id),
                FOREIGN KEY (actor_id, stage_id, actor_index)
                    REFERENCES npc_characters(actor_id, stage_id, actor_index)
            );
            CREATE TABLE font_styles (
                style_id INTEGER PRIMARY KEY NOT NULL,
                line_height INTEGER NOT NULL,
                glyph_count INTEGER NOT NULL,
                icon_count INTEGER NOT NULL
            );
            CREATE TABLE font_glyphs (
                style_id INTEGER NOT NULL,
                code INTEGER NOT NULL,
                page INTEGER NOT NULL,
                u INTEGER NOT NULL,
                v INTEGER NOT NULL,
                clut_x INTEGER NOT NULL,
                clut_y INTEGER NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                dx INTEGER NOT NULL,
                dy INTEGER NOT NULL,
                advance INTEGER NOT NULL,
                PRIMARY KEY (style_id, code),
                FOREIGN KEY (style_id) REFERENCES font_styles(style_id)
            );
            CREATE TABLE font_icons (
                style_id INTEGER NOT NULL,
                code INTEGER NOT NULL,
                page INTEGER NOT NULL,
                u INTEGER NOT NULL,
                v INTEGER NOT NULL,
                clut_x INTEGER NOT NULL,
                clut_y INTEGER NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                dx INTEGER NOT NULL,
                dy INTEGER NOT NULL,
                advance INTEGER NOT NULL,
                PRIMARY KEY (style_id, code),
                FOREIGN KEY (style_id) REFERENCES font_styles(style_id)
            );
            CREATE TABLE text_files (
                file_index INTEGER PRIMARY KEY NOT NULL,
                disc_path TEXT NOT NULL,
                entry_count INTEGER NOT NULL
            );
            CREATE TABLE dialogue_zones (
                stage_id INTEGER PRIMARY KEY NOT NULL,
                mode INTEGER UNIQUE NOT NULL,
                talk_file INTEGER NOT NULL
            );
            CREATE TABLE dialogue_actors (
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                x INTEGER NOT NULL,
                y INTEGER NOT NULL,
                direction INTEGER NOT NULL,
                width INTEGER NOT NULL,
                PRIMARY KEY (stage_id, actor_index),
                FOREIGN KEY (stage_id) REFERENCES dialogue_zones(stage_id)
            );
            CREATE TABLE dialogue_talks (
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                talk_index INTEGER NOT NULL,
                text_entry INTEGER NOT NULL,
                selected INTEGER NOT NULL,
                kind TEXT NOT NULL,
                PRIMARY KEY (stage_id, actor_index, talk_index),
                FOREIGN KEY (stage_id, actor_index) REFERENCES dialogue_actors(stage_id, actor_index)
            );
            CREATE TABLE dialogue_codes (
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                talk_index INTEGER NOT NULL,
                list_kind TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                code INTEGER NOT NULL,
                value INTEGER NOT NULL,
                kind TEXT NOT NULL,
                PRIMARY KEY (stage_id, actor_index, talk_index, list_kind, ordinal),
                FOREIGN KEY (stage_id, actor_index, talk_index) REFERENCES dialogue_talks(stage_id, actor_index, talk_index)
            );
            CREATE TABLE dialogue_talk_families (
                stage_id INTEGER NOT NULL,
                actor_index INTEGER NOT NULL,
                talk_index INTEGER NOT NULL,
                family TEXT NOT NULL,
                PRIMARY KEY (stage_id, actor_index, talk_index, family),
                FOREIGN KEY (stage_id, actor_index, talk_index) REFERENCES dialogue_talks(stage_id, actor_index, talk_index)
            );
            CREATE TABLE talk_box_frames (
                frame_id INTEGER PRIMARY KEY NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                clut_frames INTEGER NOT NULL,
                semi INTEGER NOT NULL,
                image_path TEXT NOT NULL
            );
            CREATE TABLE talk_box_layouts (
                layout INTEGER PRIMARY KEY NOT NULL,
                sprite INTEGER NOT NULL,
                sprite_x INTEGER NOT NULL,
                sprite_y INTEGER NOT NULL,
                sprite0_x INTEGER NOT NULL,
                sprite0_y INTEGER NOT NULL,
                panel_x INTEGER NOT NULL,
                panel_y INTEGER NOT NULL,
                sprite2_x INTEGER NOT NULL,
                sprite2_y INTEGER NOT NULL,
                name_x INTEGER NOT NULL,
                name_y INTEGER NOT NULL,
                text_x INTEGER NOT NULL,
                text_y INTEGER NOT NULL,
                arrow_x INTEGER NOT NULL,
                arrow_y INTEGER NOT NULL
            );
            CREATE TABLE talk_box_panel (
                red INTEGER NOT NULL,
                green INTEGER NOT NULL,
                blue INTEGER NOT NULL,
                semi INTEGER NOT NULL,
                texture_page INTEGER NOT NULL,
                clut INTEGER NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                image_path TEXT NOT NULL
            );
            CREATE TABLE encounter_enemies (
                enemy_index INTEGER PRIMARY KEY NOT NULL,
                fighter INTEGER NOT NULL,
                level INTEGER NOT NULL,
                hp INTEGER NOT NULL,
                mp INTEGER NOT NULL,
                strength INTEGER NOT NULL
            );
            CREATE TABLE encounter_records (
                encounter_index INTEGER PRIMARY KEY NOT NULL,
                enemy0 INTEGER NOT NULL,
                enemy1 INTEGER NOT NULL,
                enemy2 INTEGER NOT NULL,
                ambush_chance INTEGER NOT NULL,
                kind INTEGER NOT NULL,
                blocks TEXT NOT NULL
            );
            CREATE TABLE encounter_battle_rates (
                rate_index INTEGER PRIMARY KEY NOT NULL,
                rate INTEGER NOT NULL
            );
            CREATE TABLE encounter_stage_arrays (
                stage_id INTEGER NOT NULL,
                array_index INTEGER NOT NULL,
                addr INTEGER NOT NULL,
                entry_count INTEGER NOT NULL,
                overlay_sha256 TEXT NOT NULL,
                PRIMARY KEY (stage_id, array_index)
            );
            CREATE TABLE battle_digimon (id INTEGER PRIMARY KEY NOT NULL, name_id INTEGER NOT NULL, family INTEGER NOT NULL);
            CREATE TABLE battle_techniques (id INTEGER PRIMARY KEY NOT NULL, mp INTEGER NOT NULL, power INTEGER NOT NULL, icon INTEGER NOT NULL, kind INTEGER NOT NULL, accuracy INTEGER NOT NULL, element INTEGER NOT NULL, family INTEGER NOT NULL);
            CREATE TABLE battle_enemy_table (id INTEGER PRIMARY KEY NOT NULL, item INTEGER NOT NULL, item_chance INTEGER NOT NULL, name_id INTEGER NOT NULL, family INTEGER NOT NULL);
            CREATE TABLE battle_items (id INTEGER PRIMARY KEY NOT NULL, data_pointer INTEGER NOT NULL, price INTEGER NOT NULL, sell_price INTEGER NOT NULL, kind INTEGER NOT NULL, type INTEGER NOT NULL, flags INTEGER NULL, effect_kind INTEGER NULL, amount INTEGER NULL);
            CREATE TABLE battle_rewards (id INTEGER PRIMARY KEY NOT NULL, digimon_experience INTEGER NOT NULL, experience INTEGER NOT NULL, money INTEGER NOT NULL);
            CREATE TABLE digivolution_records (partner INTEGER NOT NULL, ordinal INTEGER NOT NULL, digimon_index INTEGER NOT NULL, digimon_id INTEGER NOT NULL, stat INTEGER NOT NULL, value INTEGER NOT NULL, PRIMARY KEY (partner, ordinal));
            CREATE TABLE digivolution_needs (partner INTEGER NOT NULL, ordinal INTEGER NOT NULL, need_ordinal INTEGER NOT NULL, digimon_index INTEGER NOT NULL, digimon_id INTEGER NOT NULL, level INTEGER NOT NULL, PRIMARY KEY (partner, ordinal, need_ordinal));
            CREATE TABLE battle_growth (table_name TEXT NOT NULL, row_ordinal INTEGER NOT NULL, value_ordinal INTEGER NOT NULL, value INTEGER NOT NULL, PRIMARY KEY (table_name, row_ordinal, value_ordinal));
            CREATE TABLE battle_text_tables (disc_path TEXT PRIMARY KEY NOT NULL, entry_count INTEGER NOT NULL);
            CREATE TABLE battle_text_tokens (disc_path TEXT NOT NULL, entry_ordinal INTEGER NOT NULL, token_ordinal INTEGER NOT NULL, kind INTEGER NOT NULL, code INTEGER NOT NULL, args_json TEXT NOT NULL, PRIMARY KEY (disc_path, entry_ordinal, token_ordinal), FOREIGN KEY (disc_path) REFERENCES battle_text_tables(disc_path));
            CREATE TABLE battle_models (fighter_id INTEGER PRIMARY KEY NOT NULL, entry_ordinal INTEGER NOT NULL, record_index INTEGER NOT NULL, kind INTEGER NOT NULL, status TEXT NOT NULL, reason TEXT NULL, is_enemy INTEGER NULL, model_file INTEGER NULL, model_entry INTEGER NULL, motion_file INTEGER NULL, motion_entry INTEGER NULL, texture_entry INTEGER NULL, bone_count INTEGER NULL, mesh_count INTEGER NULL, disc_path TEXT NULL, sha256 TEXT NULL, manifest_path TEXT NULL);
            CREATE TABLE battle_model_bones (fighter_id INTEGER NOT NULL, bone INTEGER NOT NULL, parent INTEGER NULL, mesh_entry INTEGER NULL, key_entry INTEGER NULL, PRIMARY KEY (fighter_id, bone), FOREIGN KEY (fighter_id) REFERENCES battle_models(fighter_id));
            CREATE TABLE battle_model_textures (fighter_id INTEGER NOT NULL, texture_key INTEGER NOT NULL, mode INTEGER NOT NULL, page INTEGER NOT NULL, clut_x INTEGER NULL, clut_y INTEGER NULL, transparent_pixels INTEGER NOT NULL, stp_pixels INTEGER NOT NULL, image_path TEXT NOT NULL, PRIMARY KEY (fighter_id, texture_key), FOREIGN KEY (fighter_id) REFERENCES battle_models(fighter_id));
            CREATE TABLE battle_model_track_keys (fighter_id INTEGER NOT NULL, bone INTEGER NOT NULL, track INTEGER NOT NULL, ordinal INTEGER NOT NULL, frame INTEGER NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL, PRIMARY KEY (fighter_id, bone, track, ordinal), FOREIGN KEY (fighter_id, bone) REFERENCES battle_model_bones(fighter_id, bone));
            CREATE TABLE battle_model_motion_steps (fighter_id INTEGER NOT NULL, motion INTEGER NOT NULL, ordinal INTEGER NOT NULL, step_index INTEGER NOT NULL, count INTEGER NOT NULL, frame INTEGER NOT NULL, end_frame INTEGER NOT NULL, PRIMARY KEY (fighter_id, motion, ordinal), FOREIGN KEY (fighter_id) REFERENCES battle_models(fighter_id));
            """;
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// Describes the source and completion identity persisted with a data pack.
/// </summary>
/// <param name="ExtractorVersion">Version of the extractor that produced the pack.</param>
/// <param name="DiscSha1">SHA-1 fingerprint of the user-supplied disc image.</param>
/// <param name="Edition">Edition identifier recognized by the extractor.</param>
/// <param name="CompletedAt">UTC timestamp at which validation completed.</param>
public sealed record DataPackMetadata(
    string ExtractorVersion,
    string DiscSha1,
    string Edition,
    DateTimeOffset CompletedAt);
