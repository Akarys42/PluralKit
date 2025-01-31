package main

import (
	"context"
	"fmt"
	"log"
)

func task_main() {
	log.Println("running per-minute scheduled tasks")

	update_db_meta()
	update_stats()
}

var table_stat_keys = []string{"system", "member", "group", "switch", "message"}

func plural(key string) string {
	if key[len(key)-1] == 'h' {
		return key + "es"
	}
	return key + "s"
}

func update_db_meta() {
	log.Println("updating database stats")
	for _, key := range table_stat_keys {
		if key == "message" {
			// updating message count from data db takes way too long, so we do it on a separate timer (every 10 minutes)
			continue
		}
		q := fmt.Sprintf("update info set %s_count = (select count(*) from %s)", key, plural(key))
		log.Println("data db query:", q)
		run_simple_pg_query(data_db, q)
	}
}

func update_db_message_meta() {
	count := get_message_count()

	_, err := data_db.Exec(context.Background(), "update info set message_count = $1", count)
	if err != nil {
		panic(err)
	}
}

func update_stats() {
	redisStats := run_redis_query()

	guild_count := 0
	channel_count := 0

	for _, v := range redisStats {
		log.Println(v.GuildCount, v.ChannelCount)
		guild_count += v.GuildCount
		channel_count += v.ChannelCount
	}

	do_stats_insert("guilds", guild_count)
	do_stats_insert("channels", channel_count)

	data_stats := run_data_stats_query()
	for _, key := range table_stat_keys {
		val := data_stats[key+"_count"].(int32)
		do_stats_insert(plural(key), int(val))
	}
}
